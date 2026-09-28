#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
  Prove a packed release the way a consumer meets it, before any package reaches a feed.

.DESCRIPTION
  Every consumer inside this repository — the examples, the template and libraries/ — references
  the previous release as a package, so nothing in the solution build or the suite ever imports the
  build/ files of the release being cut. A targets file that is unloadable, or that loads and does
  the wrong thing, is green everywhere here and fails in every consumer's first build. This script is
  the consumer: over the packed set it is handed, it

    1. refuses a version that cannot prove the packed set (see -Version);
    2. runs verify-packed-assembly-versions.ps1 over it;
    3. installs the packed Vion.Dale.Cli into a tool path of its own and checks it is that package;
    4. scaffolds a project with that CLI's `dale new`, asserts every Vion.Dale.* reference it wrote
       is the packed version, and that the restore `dale new` ran succeeded — the command only
       warns when it does not;
    5. runs `dale build`, `dale test` (the scaffold's own tests, on the packed test kits) and
       `dale pack`, and asserts the package carries tools/publish/<ProjectName>.json naming every
       logic block `dale new` reported;
    6. builds a copy of libraries/Vion.Diagnostics with its Vion.Dale.* pins set to the packed
       version, and runs its tests;
    7. asserts every Vion.Dale.* package restored anywhere above came from the packed set.

  A failure names the step it happened in, and the tool output above it names the cause.

  Nothing it does reaches the machine it runs on. The work directory is a fresh one under the
  system temp directory; the NuGet configuration there clears every inherited source and maps
  Vion.Dale.* to the packed set alone; the global packages folder, the dotnet home that holds the
  `dotnet new` template store and the tool install all live inside it. A package restored once stays
  in a global packages folder, so a shared one would serve an earlier run's build of the same
  version and the resolution check could not tell.

  It runs in publish.yml between Pack and either push on a release tag, as publish-nuget.yml's
  verify-script, and in release-smoke.yml on a manual dispatch.

.PARAMETER PackagesDir
  The directory holding the packed .nupkg set. Defaults to $env:PACKAGES_DIR, which is how
  publish-nuget.yml hands it over.

.PARAMETER Version
  The version the set was packed at. Defaults to $env:PACKAGE_VERSION. A 0.0.0* version is
  refused: Vion.Dale.Cli leaves its bundled template's references alone at such a version, so
  `dale new` would scaffold the checked-in ones, which restore the previous release from nuget.org —
  a green run over packages nobody is releasing.

.PARAMETER KeepWorkDir
  Keep the work directory after a green run. A failed run always keeps it and prints its path.

.PARAMETER SelfTest
  Verify this script's own checks against fixtures instead of a packed set. Needs no build, no
  network and no dotnet.

.EXAMPLE
  dotnet build Vion.Dale.Sdk.sln -c Release -p:Version=0.15.0-smoke.1
  dotnet pack Vion.Dale.Sdk.sln -c Release --no-build -p:Version=0.15.0-smoke.1 -o ./artifacts
  pwsh -NoProfile -File scripts/release-smoke.ps1 -PackagesDir ./artifacts -Version 0.15.0-smoke.1
  A manual run at the desk: pack at a release-shaped version, then prove it. Pushes nothing.
#>
[CmdletBinding()]
param(
    [string]$PackagesDir = $env:PACKAGES_DIR,
    [string]$Version = $env:PACKAGE_VERSION,
    [switch]$KeepWorkDir,
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:step = 'inputs'
$script:pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }

function Enter-Step([string]$name) {
    $script:step = $name
    Write-Host ''
    Write-Host "=== release-smoke: $name"
}

function Get-VersionRefusal([string]$candidate) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return 'no version given: pass -Version or set PACKAGE_VERSION.'
    }
    if ($candidate -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        return "'$candidate' is not a SemVer version (X.Y.Z or X.Y.Z-suffix)."
    }
    if ($candidate.StartsWith('0.0.0')) {
        return "'$candidate' is not release-shaped. Vion.Dale.Cli leaves its bundled template's references alone at a 0.0.0* version, so `dale new` would scaffold the checked-in ones and restore the previous release from nuget.org: a green run over packages nobody is releasing. Pack at a release-shaped version, for example 0.15.0-smoke.1."
    }
    return $null
}

function Get-NormalizedPath([string]$path) {
    [System.IO.Path]::GetFullPath($path).TrimEnd([char[]]@('/', '\'))
}

# A restore records a package's content hash in .nupkg.metadata as the base64 SHA-512 of the
# .nupkg's bytes, so an equal hash means the restored package is byte-for-byte the packed one.
function Get-NuGetContentHash([string]$nupkgPath) {
    [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($nupkgPath)))
}

function Write-SmokeNuGetConfig([string]$path, [string]$packagesDir) {
    $escaped = [System.Security.SecurityElement]::Escape($packagesDir)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release" value="$escaped" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="release">
      <package pattern="Vion.Dale.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}

# Reads every Vion.Dale.* PackageReference under $root, in either spelling of its version: the
# attribute and the child element. A reference with no version at all is reported as such rather
# than skipped, since central package management would resolve it from somewhere this does not read.
function Get-VionDaleReferences([string]$root) {
    foreach ($csproj in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File) {
        [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw
        foreach ($reference in $xml.SelectNodes('//*[local-name()="PackageReference"]')) {
            $id = $reference.GetAttribute('Include')
            if (-not $id.StartsWith('Vion.Dale.', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $version = $reference.GetAttribute('Version')
            if (-not $version) {
                $child = $reference.SelectSingleNode('*[local-name()="Version"]')
                $version = if ($child) { $child.InnerText.Trim() } else { '' }
            }
            [pscustomobject]@{ Project = $csproj.FullName; Id = $id; Version = $version }
        }
    }
}

function Assert-VionDaleReferences([string]$root, [string]$expected, [string]$what) {
    $references = @(Get-VionDaleReferences $root)
    if ($references.Count -eq 0) {
        throw "$what carries no Vion.Dale.* PackageReference under $root, so nothing it builds would exercise the packed set."
    }
    $wrong = @($references | Where-Object Version -ne $expected)
    if ($wrong.Count -gt 0) {
        $list = ($wrong | ForEach-Object { "$($_.Id) '$($_.Version)' in $([System.IO.Path]::GetFileName($_.Project))" }) -join '; '
        throw "$what references Vion.Dale.* at a version other than $expected`: $list."
    }
    Write-Host "  $($references.Count) Vion.Dale.* reference(s) at $expected across $(@($references | Select-Object -ExpandProperty Project -Unique).Count) project(s)."
}

# The same rewrite set-version.ps1 applies, over a copy: the attribute spelling only. Any reference
# in another spelling is left alone and then named by the assertion that follows it.
function Set-VionDaleReferences([string]$root, [string]$version) {
    foreach ($csproj in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File) {
        $text = [System.IO.File]::ReadAllText($csproj.FullName)
        $rewritten = [regex]::Replace($text, '(<PackageReference\s+Include="Vion\.Dale\.[^"]*"\s+Version=")[^"]*(")', "`${1}$version`${2}")
        if ($rewritten -ne $text) { [System.IO.File]::WriteAllText($csproj.FullName, $rewritten) }
    }
}

# `dale new` warns and exits 0 when its restore fails, so its exit code says nothing about the
# restore. The assets file does: restore writes one per project, carrying its errors in `logs`.
function Assert-Restored([string]$root, [string]$what) {
    $projects = @(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File)
    foreach ($csproj in $projects) {
        $assets = Join-Path $csproj.DirectoryName 'obj/project.assets.json'
        if (-not (Test-Path -LiteralPath $assets)) {
            throw "$what left $($csproj.Name) unrestored: no obj/project.assets.json."
        }
        $json = Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json
        $logs = if ($json.PSObject.Properties['logs']) { @($json.logs) } else { @() }
        $errors = @($logs | Where-Object { $_.level -eq 'Error' })
        if ($errors.Count -gt 0) {
            throw "$what could not restore $($csproj.Name): $(($errors | ForEach-Object { "$($_.code) $($_.message)" }) -join ' | ')"
        }
    }
    Write-Host "  $($projects.Count) project(s) restored without an error."
}

function Assert-TestsRan([string]$resultsDir, [string]$what) {
    $trx = @(Get-ChildItem -LiteralPath $resultsDir -Recurse -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    if ($trx.Count -eq 0) { throw "$what wrote no test results under $resultsDir." }
    $total = 0; $failed = 0
    foreach ($file in $trx) {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        $counters = $xml.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
        if (-not $counters) { throw "$($file.Name) carries no result counters." }
        $total += [int]$counters.GetAttribute('total')
        $failed += [int]$counters.GetAttribute('failed')
    }
    if ($total -eq 0) { throw "$what ran zero tests: a green exit over no tests proves nothing." }
    if ($failed -gt 0) { throw "$what failed $failed of $total tests." }
    Write-Host "  $total test(s) ran, none failed."
}

function Read-ZipText([string]$zipPath, [string]$entryName) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entry = $zip.Entries | Where-Object FullName -eq $entryName
        if (-not $entry) { return $null }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

function Assert-PublishedBlocks([string]$nupkg, [string]$projectName, [string[]]$blocks) {
    $entryName = "tools/publish/$projectName.json"
    $text = Read-ZipText $nupkg $entryName
    if ($null -eq $text) {
        throw "$([System.IO.Path]::GetFileName($nupkg)) carries no $entryName, the file the SDK's build targets exist to produce."
    }
    $document = $text | ConvertFrom-Json
    $published = @($document.logicBlocks | ForEach-Object { ($_.typeFullName -split '\.')[-1] })
    $missing = @($blocks | Where-Object { $published -notcontains $_ })
    if ($blocks.Count -eq 0) { throw '`dale new` reported no logic blocks, so there is nothing to find in the package.' }
    if ($missing.Count -gt 0) {
        throw "$entryName names [$($published -join ', ')], missing the scaffolded [$($missing -join ', ')]."
    }
    Write-Host "  $entryName names $($published -join ', ')."
}

# Every Vion.Dale.* package in the smoke's own global packages folder was restored by this run, since
# the folder starts empty. Each must be the packed version, recorded as coming from the packed set,
# and byte-for-byte the packed file.
function Assert-Resolution([string]$globalPackages, [string]$packagesDir, [string]$version, [string]$workDir) {
    $expectedSource = Get-NormalizedPath $packagesDir
    $restored = @(Get-ChildItem -LiteralPath $globalPackages -Directory -Filter 'vion.dale.*' -ErrorAction SilentlyContinue)
    if ($restored.Count -eq 0) { throw "no Vion.Dale.* package was restored into $globalPackages." }
    foreach ($idDir in $restored) {
        foreach ($versionDir in Get-ChildItem -LiteralPath $idDir.FullName -Directory) {
            if ($versionDir.Name -ne $version.ToLowerInvariant()) {
                throw "$($idDir.Name) was restored at $($versionDir.Name), not $version."
            }
            $metadataPath = Join-Path $versionDir.FullName '.nupkg.metadata'
            if (-not (Test-Path -LiteralPath $metadataPath)) { throw "$($idDir.Name) $($versionDir.Name) has no .nupkg.metadata." }
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            $source = if ($metadata.PSObject.Properties['source']) { $metadata.source } else { '' }
            if (-not $source -or -not (Get-NormalizedPath $source).Equals($expectedSource, $script:pathComparison)) {
                throw "$($idDir.Name) $($versionDir.Name) was restored from '$source', not from the packed set at $expectedSource."
            }
            $packed = @(Get-ChildItem -LiteralPath $packagesDir -Filter '*.nupkg' -File | Where-Object { $_.Name -ieq "$($idDir.Name).$version.nupkg" })
            if ($packed.Count -ne 1) { throw "$($idDir.Name) $version was restored, but the packed set holds no $($idDir.Name).$version.nupkg." }
            if ($metadata.contentHash -ne (Get-NuGetContentHash $packed[0].FullName)) {
                throw "$($idDir.Name) $version in $globalPackages differs from $($packed[0].Name) in the packed set."
            }
        }
    }

    $assetsFiles = @(Get-ChildItem -LiteralPath $workDir -Recurse -Filter 'project.assets.json' -File |
        Where-Object { -not $_.FullName.StartsWith($globalPackages, $script:pathComparison) })
    $resolved = @{}
    foreach ($assets in $assetsFiles) {
        $json = Get-Content -LiteralPath $assets.FullName -Raw | ConvertFrom-Json
        foreach ($library in @($json.libraries.PSObject.Properties | ForEach-Object Name)) {
            $id, $libraryVersion = $library -split '/', 2
            if (-not $id.StartsWith('Vion.Dale.', [StringComparison]::OrdinalIgnoreCase)) { continue }
            if ($libraryVersion -ne $version) {
                throw "$($assets.Directory.Parent.Name) resolved $id at $libraryVersion, not $version."
            }
            $resolved[$id] = $true
        }
    }
    Write-Host "  $($restored.Count) Vion.Dale.* package(s) restored, each from $expectedSource at $version and identical to the packed file; $($resolved.Count) id(s) resolved across $($assetsFiles.Count) project(s)."
}

function Invoke-Native([string]$what, [string]$file, [string[]]$arguments) {
    Write-Host "  > $what"
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$what exited $LASTEXITCODE; the output above names the cause." }
}

function Copy-Tree([string]$from, [string]$to) {
    $prefix = (Get-NormalizedPath $from).Length + 1
    foreach ($file in Get-ChildItem -LiteralPath $from -Recurse -File) {
        $relative = $file.FullName.Substring($prefix)
        if ($relative -match '(^|[\\/])(bin|obj)([\\/])') { continue }
        $target = Join-Path $to $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Invoke-Smoke {
    Enter-Step 'inputs'
    $refusal = Get-VersionRefusal $Version
    if ($refusal) { throw $refusal }
    if ([string]::IsNullOrWhiteSpace($PackagesDir) -or -not (Test-Path -LiteralPath $PackagesDir -PathType Container)) {
        throw "the packed set '$PackagesDir' is not a directory: pass -PackagesDir or set PACKAGES_DIR."
    }
    $packages = Get-NormalizedPath (Resolve-Path -LiteralPath $PackagesDir).Path
    $cliPackage = Join-Path $packages "Vion.Dale.Cli.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $cliPackage)) {
        throw "the packed set at $packages holds no Vion.Dale.Cli.$Version.nupkg, so there is no CLI at that version to prove it with."
    }
    Write-Host "  $(@(Get-ChildItem -LiteralPath $packages -Filter '*.nupkg' -File).Count) package(s) at $Version in $packages."

    Enter-Step 'packed assembly versions'
    Invoke-Native 'verify-packed-assembly-versions.ps1' 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-packed-assembly-versions.ps1'), '-PackagesDir', $packages)

    # A short root: MSBuild on Windows fails with MSB3106 and CS0012 on a restored assembly whose
    # full path passes 260 characters, and the global packages folder nests four levels below it.
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('dale-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $work | Out-Null
    $script:workDir = $work
    $globalPackages = Join-Path $work 'packages'
    Write-SmokeNuGetConfig (Join-Path $work 'nuget.config') $packages
    $env:NUGET_PACKAGES = $globalPackages
    $env:DOTNET_CLI_HOME = Join-Path $work 'home'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    # A build node or compiler server left running keeps files in the global packages folder open,
    # so the work directory could not be removed after a green run.
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:UseSharedCompilation = 'false'
    Write-Host "  work directory: $work"

    Enter-Step 'packed CLI'
    $toolPath = Join-Path $work 'tools'
    Invoke-Native "dotnet tool install Vion.Dale.Cli $Version" 'dotnet' @('tool', 'install', 'Vion.Dale.Cli', '--version', $Version, '--tool-path', $toolPath, '--configfile', (Join-Path $work 'nuget.config'))
    # The tool store keeps a byte copy of the package it installed. Its .nupkg.metadata hash is not
    # the file's SHA-512, unlike a restore's, so the copy is what gets compared.
    $lowerVersion = $Version.ToLowerInvariant()
    $storeCopy = Join-Path $toolPath ".store/vion.dale.cli/$lowerVersion/vion.dale.cli/$lowerVersion/vion.dale.cli.$lowerVersion.nupkg"
    if (-not (Test-Path -LiteralPath $storeCopy)) { throw "the installed tool has no $storeCopy." }
    if ((Get-NuGetContentHash $storeCopy) -ne (Get-NuGetContentHash $cliPackage)) {
        throw "the installed Vion.Dale.Cli differs from $([System.IO.Path]::GetFileName($cliPackage)) in the packed set."
    }
    $dale = Join-Path $toolPath ($IsWindows ? 'dale.exe' : 'dale')
    $reported = (& $dale --version) -join ' '
    if ($LASTEXITCODE -ne 0 -or $reported -notmatch "(^|\s)$([regex]::Escape($Version))(\s|$)") {
        throw "the installed dale reports '$reported', not $Version."
    }
    Write-Host "  $reported"

    Enter-Step 'dale new'
    $projectName = 'SmokeLib'
    Push-Location $work
    try {
        Write-Host "  > dale new $projectName --no-interactive --output json"
        $newOutput = & $dale new $projectName --no-interactive --output json
        if ($LASTEXITCODE -ne 0) { throw "dale new exited $LASTEXITCODE`: $($newOutput -join ' ')" }
    }
    finally { Pop-Location }
    $scaffold = Join-Path $work $projectName
    $blocks = @((($newOutput -join "`n") | ConvertFrom-Json).logicBlocks)
    Write-Host "  dale new reported logic block(s): $($blocks -join ', ')"
    Assert-VionDaleReferences $scaffold $Version 'the scaffold'
    Assert-Restored $scaffold 'dale new'

    Push-Location $scaffold
    try {
        Enter-Step 'dale build'
        Invoke-Native 'dale build' $dale @('build')

        Enter-Step 'dale test'
        $results = Join-Path $work 'results/scaffold'
        Invoke-Native 'dale test' $dale @('test', '--logger', 'trx', '--results-directory', $results)
        Assert-TestsRan $results 'the scaffold''s tests'

        Enter-Step 'dale pack'
        Invoke-Native 'dale pack' $dale @('pack', '--project', (Join-Path $projectName "$projectName.csproj"))
    }
    finally { Pop-Location }
    $produced = @(Get-ChildItem -LiteralPath (Join-Path $scaffold "$projectName/bin/Release") -Filter "$projectName.*.nupkg" -File |
        Where-Object Name -notlike '*.snupkg')
    if ($produced.Count -ne 1) { throw "dale pack left $($produced.Count) $projectName package(s) in bin/Release, expected one." }
    Assert-PublishedBlocks $produced[0].FullName $projectName $blocks

    Enter-Step 'Vion.Diagnostics build'
    $library = Join-Path $work 'Vion.Diagnostics'
    Copy-Tree (Join-Path (Split-Path $PSScriptRoot) 'libraries/Vion.Diagnostics') $library
    Set-VionDaleReferences $library $Version
    Assert-VionDaleReferences $library $Version 'libraries/Vion.Diagnostics'
    $solution = Join-Path $library 'Vion.Diagnostics.sln'
    Invoke-Native 'dotnet build Vion.Diagnostics.sln' 'dotnet' @('build', $solution, '-c', 'Release')

    Enter-Step 'Vion.Diagnostics test'
    $results = Join-Path $work 'results/diagnostics'
    Invoke-Native 'dotnet test Vion.Diagnostics.sln' 'dotnet' @('test', $solution, '-c', 'Release', '--no-build', '--logger', 'trx', '--results-directory', $results)
    Assert-TestsRan $results 'Vion.Diagnostics''s tests'

    Enter-Step 'resolution'
    Assert-Resolution $globalPackages $packages $Version $work
}

function Invoke-SelfTest {
    $failures = [System.Collections.Generic.List[string]]::new()
    function Expect-Throw([string]$name, [scriptblock]$block, [string]$pattern) {
        try { & $block; $failures.Add("$name`: expected a failure matching '$pattern', got none.") }
        catch { if ($_.Exception.Message -notmatch $pattern) { $failures.Add("$name`: failure '$($_.Exception.Message)' does not match '$pattern'.") } }
    }
    function Expect-Pass([string]$name, [scriptblock]$block) {
        try { & $block | Out-Null } catch { $failures.Add("$name`: unexpected failure '$($_.Exception.Message)'.") }
    }

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('release-smoke-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        foreach ($case in @(
                @{ Version = ''; Pattern = 'no version given' },
                @{ Version = 'banana'; Pattern = 'not a SemVer' },
                @{ Version = '0.0.0-ci.42'; Pattern = 'not release-shaped' },
                @{ Version = '0.0.0'; Pattern = 'not release-shaped' })) {
            $refusal = Get-VersionRefusal $case.Version
            if ($refusal -notmatch $case.Pattern) { $failures.Add("version '$($case.Version)': refusal '$refusal' does not match '$($case.Pattern)'.") }
        }
        foreach ($accepted in @('0.15.0', '0.15.0-smoke.1', '1.0.0-preview.2')) {
            if (Get-VersionRefusal $accepted) { $failures.Add("version '$accepted' was refused.") }
        }

        # The whole script, as publish-nuget.yml runs it: a refused version fails in the inputs step,
        # before anything is installed.
        $env:PACKAGES_DIR = $tmp
        $env:PACKAGE_VERSION = '0.0.0-ci.7'
        $output = & pwsh -NoProfile -File $PSCommandPath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $output -notmatch "FAILED at 'inputs'.*not release-shaped") {
            $failures.Add("a 0.0.0-ci version run: exit $LASTEXITCODE, output: $output")
        }
        $env:PACKAGE_VERSION = '0.15.0'
        $output = & pwsh -NoProfile -File $PSCommandPath 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $output -notmatch "FAILED at 'inputs'.*no Vion\.Dale\.Cli\.0\.15\.0\.nupkg") {
            $failures.Add("a packed set without the CLI: exit $LASTEXITCODE, output: $output")
        }
        Remove-Item Env:PACKAGES_DIR, Env:PACKAGE_VERSION

        $projects = Join-Path $tmp 'projects'
        New-Item -ItemType Directory -Path (Join-Path $projects 'A'), (Join-Path $projects 'B') | Out-Null
        Set-Content -LiteralPath (Join-Path $projects 'A/A.csproj') -Value '<Project><ItemGroup><PackageReference Include="Vion.Dale.Sdk" Version="0.14.2"/><PackageReference Include="xunit.v3" Version="3.2.0"/></ItemGroup></Project>'
        Set-Content -LiteralPath (Join-Path $projects 'B/B.csproj') -Value '<Project><ItemGroup><PackageReference Include="Vion.Dale.Sdk.TestKit"><Version>0.14.2</Version></PackageReference></ItemGroup></Project>'
        Expect-Throw 'a pin at the previous release' { Assert-VionDaleReferences $projects '0.15.0' 'fixture' } 'Vion\.Dale\.Sdk ''0\.14\.2'''
        Set-VionDaleReferences $projects '0.15.0'
        Expect-Throw 'a child-element version the rewrite does not reach' { Assert-VionDaleReferences $projects '0.15.0' 'fixture' } 'Vion\.Dale\.Sdk\.TestKit ''0\.14\.2'' in B\.csproj'
        if ((Get-Content -LiteralPath (Join-Path $projects 'A/A.csproj') -Raw) -notmatch 'Include="xunit\.v3" Version="3\.2\.0"') {
            $failures.Add('the rewrite touched a reference outside Vion.Dale.*.')
        }
        Set-Content -LiteralPath (Join-Path $projects 'B/B.csproj') -Value '<Project><ItemGroup><PackageReference Include="Vion.Dale.Sdk.TestKit" Version="0.15.0"/></ItemGroup></Project>'
        Expect-Pass 'every pin at the version' { Assert-VionDaleReferences $projects '0.15.0' 'fixture' }
        $empty = Join-Path $tmp 'empty'; New-Item -ItemType Directory -Path $empty | Out-Null
        Set-Content -LiteralPath (Join-Path $empty 'E.csproj') -Value '<Project/>'
        Expect-Throw 'no Vion.Dale.* reference at all' { Assert-VionDaleReferences $empty '0.15.0' 'fixture' } 'no Vion\.Dale\.\* PackageReference'

        $obj = Join-Path $projects 'A/obj'; New-Item -ItemType Directory -Path $obj, (Join-Path $projects 'B/obj') | Out-Null
        Set-Content -LiteralPath (Join-Path $obj 'project.assets.json') -Value '{"version":3,"libraries":{},"logs":[{"code":"NU1101","level":"Error","message":"Unable to find package Vion.Dale.Sdk."}]}'
        Set-Content -LiteralPath (Join-Path $projects 'B/obj/project.assets.json') -Value '{"version":3,"libraries":{}}'
        Expect-Throw 'a restore that logged an error' { Assert-Restored $projects 'fixture' } 'NU1101'
        Remove-Item -LiteralPath (Join-Path $projects 'B/obj/project.assets.json')
        Set-Content -LiteralPath (Join-Path $obj 'project.assets.json') -Value '{"version":3,"libraries":{}}'
        Expect-Throw 'a project never restored' { Assert-Restored $projects 'fixture' } 'B\.csproj unrestored'

        $results = Join-Path $tmp 'results'; New-Item -ItemType Directory -Path $results | Out-Null
        Expect-Throw 'no results written' { Assert-TestsRan $results 'fixture' } 'wrote no test results'
        $trx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="{0}" failed="{1}"/></ResultSummary></TestRun>'
        Set-Content -LiteralPath (Join-Path $results 'a.trx') -Value ($trx -f 0, 0)
        Expect-Throw 'zero tests' { Assert-TestsRan $results 'fixture' } 'ran zero tests'
        Set-Content -LiteralPath (Join-Path $results 'a.trx') -Value ($trx -f 3, 0)
        Expect-Pass 'three tests, none failed' { Assert-TestsRan $results 'fixture' }

        $nupkgDir = Join-Path $tmp 'nupkg'; New-Item -ItemType Directory -Path (Join-Path $nupkgDir 'tools/publish') | Out-Null
        Set-Content -LiteralPath (Join-Path $nupkgDir 'tools/publish/Lib.json') -Value '{"logicBlocks":[{"typeFullName":"Lib.Thermostat"}]}'
        $withJson = Join-Path $tmp 'with.nupkg'
        [System.IO.Compression.ZipFile]::CreateFromDirectory($nupkgDir, $withJson)
        Expect-Pass 'the scaffolded block published' { Assert-PublishedBlocks $withJson 'Lib' @('Thermostat') }
        Expect-Throw 'a scaffolded block missing' { Assert-PublishedBlocks $withJson 'Lib' @('Thermostat', 'Pump') } 'missing the scaffolded \[Pump\]'
        Expect-Throw 'no published document' { Assert-PublishedBlocks $withJson 'Other' @('Thermostat') } 'carries no tools/publish/Other\.json'

        # A packed set, a global packages folder and an assets file, as a restore leaves them.
        $packed = Join-Path $tmp 'packed'; New-Item -ItemType Directory -Path $packed | Out-Null
        $sdkNupkg = Join-Path $packed 'Vion.Dale.Sdk.0.15.0.nupkg'
        [System.IO.File]::WriteAllBytes($sdkNupkg, [byte[]](1, 2, 3))
        $global = Join-Path $tmp 'global'
        $restoredDir = Join-Path $global 'vion.dale.sdk/0.15.0'
        New-Item -ItemType Directory -Path $restoredDir | Out-Null
        $work = Join-Path $tmp 'work/Lib/obj'; New-Item -ItemType Directory -Path $work | Out-Null
        Set-Content -LiteralPath (Join-Path $work 'project.assets.json') -Value '{"libraries":{"Vion.Dale.Sdk/0.15.0":{},"xunit.v3/3.2.0":{}}}'
        function Write-Metadata([string]$source, [string]$hash) {
            @{ version = 2; contentHash = $hash; source = $source } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $restoredDir '.nupkg.metadata')
        }
        $hash = Get-NuGetContentHash $sdkNupkg
        Write-Metadata $packed $hash
        Expect-Pass 'restored from the packed set' { Assert-Resolution $global $packed '0.15.0' (Join-Path $tmp 'work') }
        Write-Metadata 'https://api.nuget.org/v3/index.json' $hash
        Expect-Throw 'restored from a feed' { Assert-Resolution $global $packed '0.15.0' (Join-Path $tmp 'work') } 'restored from ''https://api\.nuget\.org'
        Write-Metadata $packed 'AAAA'
        Expect-Throw 'a different file at the same version' { Assert-Resolution $global $packed '0.15.0' (Join-Path $tmp 'work') } 'differs from Vion\.Dale\.Sdk\.0\.15\.0\.nupkg'
        Write-Metadata $packed $hash
        Set-Content -LiteralPath (Join-Path $work 'project.assets.json') -Value '{"libraries":{"Vion.Dale.Sdk/0.14.2":{}}}'
        Expect-Throw 'a project resolving the previous release' { Assert-Resolution $global $packed '0.15.0' (Join-Path $tmp 'work') } 'resolved Vion\.Dale\.Sdk at 0\.14\.2'
        Expect-Throw 'nothing restored' { Assert-Resolution (Join-Path $tmp 'nowhere') $packed '0.15.0' (Join-Path $tmp 'work') } 'no Vion\.Dale\.\* package was restored'
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "  FAIL  $_" }
        Write-Host "release-smoke self-test: $($failures.Count) failure(s)"
        exit 1
    }
    Write-Host 'release-smoke self-test: all cases passed'
    exit 0
}

if ($SelfTest) { Invoke-SelfTest }

$script:workDir = $null
$saved = @{}
foreach ($name in 'NUGET_PACKAGES', 'DOTNET_CLI_HOME', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'MSBUILDDISABLENODEREUSE', 'UseSharedCompilation') {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$failed = $false
try {
    Invoke-Smoke
}
catch {
    $failed = $true
    Write-Host ''
    Write-Host "release-smoke: FAILED at '$($script:step)': $($_.Exception.Message)"
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

if ($failed) {
    if ($script:workDir) { Write-Host "release-smoke: the work directory is kept at $($script:workDir)" }
    exit 1
}
if ($script:workDir -and -not $KeepWorkDir) {
    Remove-Item -LiteralPath $script:workDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $script:workDir) { Write-Host "release-smoke: could not remove all of $($script:workDir)" }
}
elseif ($script:workDir) {
    Write-Host "release-smoke: the work directory is kept at $($script:workDir)"
}
Write-Host ''
Write-Host "release-smoke: PASSED: $Version installs, scaffolds, builds, tests and packs as a consumer meets it, and Vion.Diagnostics builds and passes its tests on it."
