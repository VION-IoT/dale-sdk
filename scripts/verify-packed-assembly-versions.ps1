#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fail when a packed assembly's AssemblyVersion does not match the version of the
  package that carries it — the shape release 0.11.1 shipped.

.DESCRIPTION
  0.11.1 published Vion.Dale.Sdk, .DigitalIo and .AnalogIo with correct nuspecs and
  lib assemblies stamped 0.0.0.0, while ProtoActor/Plugin/Http were stamped 0.11.1.0
  and bind to `Vion.Dale.Sdk, Version=0.11.1.0`. Every consumer of the lib assets died
  at startup with FileNotFoundException. Nothing in the pipeline could contradict it:
  the packages were well-formed, the tests were green, only the dll bytes were stale.

  This is the gate for that class, not for its one cause. A packed assembly's
  AssemblyVersion is the numeric prefix of its package version padded to four parts
  (`0.11.1` -> `0.11.1.0`, `0.0.0-ci.42` -> `0.0.0.0`) — that is what the .NET SDK
  derives from `-p:Version`, so any assembly in the package that disagrees was built
  by something other than the pack that produced the nuspec.

  Scope: `lib/` and `analyzers/`. `analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll`
  was stamped 0.0.0.0 in 0.11.1 too — same defect, same package. `tools/` is left
  alone: it is a publish folder, legitimately full of third-party assemblies carrying
  their own versions.

  It also fails a package that is missing content its id promises ($requiredPackageContent):
  today, a `Vion.Dale.Sdk` with no analyzer assembly. That package restores and compiles
  clean while judging nothing, so a consumer sees it as previously-red code turning green.

  Within those folders only assemblies BELONGING to the package are judged — simple
  name equal to the package id, or beginning with the package id and a dot. Anything
  else is listed as unchecked rather than trusted silently or failed wrongly, so a
  package that starts shipping a foreign assembly shows up in the output.

  Uses System.IO.Compression so it runs the same on Windows and the Linux CI runner.

.PARAMETER PackagesDir
  Directory containing the .nupkg files to verify (e.g. ./artifacts). Fails when it
  holds no packages — an empty artifact must not pass as "nothing wrong".

.PARAMETER SelfTest
  Verify this script instead of a release: builds a matching and a mismatching package
  in a temp directory and asserts the verdict on each. Needs no network and no build.

.EXAMPLE
  pwsh scripts/verify-packed-assembly-versions.ps1 -PackagesDir ./artifacts
  The CI gate: exit 1 if any packed assembly is stale.

.EXAMPLE
  pwsh scripts/verify-packed-assembly-versions.ps1 -SelfTest
  Check the gate itself.
#>
[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [string]$PackagesDir,

    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

# Content a package must carry because its id promises it, keyed on the package id. A package not
# named here is judged for assembly versions only. Vion.Dale.Sdk packs its analyzer under a
# Condition="Exists(...)" (Vion.Dale.Sdk.csproj), so a build that did not produce the assembly packs
# a package that restores, compiles clean and judges nothing — the only signal being previously-red
# code turning green. The gate for that is here because the artifact is the only place it shows.
$requiredPackageContent = @{
    'Vion.Dale.Sdk' = @('analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll')
}

# The nuspec version's numeric prefix, padded to the four parts an AssemblyVersion always has.
function Get-ExpectedAssemblyVersion([string]$packageVersion)
{
    $numeric = ($packageVersion -split '[-+]', 2)[0]
    $parts = @($numeric -split '\.') + @('0', '0', '0', '0')
    return [Version]::Parse(($parts[0..3] -join '.'))
}

function Get-PackedAssemblyVersion($entry)
{
    # AssemblyName.GetAssemblyName reads metadata off disk, so the entry has to land there first.
    $file = Join-Path ([System.IO.Path]::GetTempPath()) ("dale-gate-" + [Guid]::NewGuid().ToString('N') + ".dll")
    try
    {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file, $true)
        return [System.Reflection.AssemblyName]::GetAssemblyName($file).Version
    }
    catch [BadImageFormatException]
    {
        return $null
    }
    finally
    {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

# Every finding for one package: mismatches to fail on, and assemblies deliberately not judged.
function Test-Package([string]$nupkgPath)
{
    $mismatches = @()
    $checked = 0
    $unchecked = @()
    $required = @()
    $missing = @()

    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try
    {
        $nuspec = $zip.Entries | Where-Object { $_.FullName -eq $_.Name -and $_.Name -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspec)
        {
            throw "No .nuspec at the root of $nupkgPath."
        }

        $reader = New-Object System.IO.StreamReader($nuspec.Open())
        try
        {
            $metadata = ([xml]$reader.ReadToEnd()).package.metadata
        }
        finally
        {
            $reader.Dispose()
        }

        $packageId = $metadata.id
        $packageVersion = $metadata.version
        $expected = Get-ExpectedAssemblyVersion $packageVersion

        # Entry names inside a .nupkg are zip paths, forward-slashed on every platform. The comparison
        # is -cnotcontains because PowerShell's -contains is case-insensitive and the convention path
        # is not: a package carrying Analyzers/Dotnet/Cs/... restores and loads no analyzer.
        $required = if ($requiredPackageContent.ContainsKey($packageId)) { @($requiredPackageContent[$packageId]) } else { @() }
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
        $missing = @($required | Where-Object { $entryNames -cnotcontains $_ })

        foreach ($entry in $zip.Entries)
        {
            if ($entry.FullName -notlike 'lib/*' -and $entry.FullName -notlike 'analyzers/*') { continue }
            if ($entry.Name -notlike '*.dll') { continue }

            $simpleName = [System.IO.Path]::GetFileNameWithoutExtension($entry.Name)
            if ($simpleName -ne $packageId -and -not $simpleName.StartsWith("$packageId."))
            {
                $unchecked += "$($entry.FullName) (not an assembly of $packageId)"
                continue
            }

            $actual = Get-PackedAssemblyVersion $entry
            if ($null -eq $actual)
            {
                $unchecked += "$($entry.FullName) (not a managed assembly)"
                continue
            }

            $checked++
            if ($actual -ne $expected)
            {
                $mismatches += "  $packageId $packageVersion -> $($entry.FullName): AssemblyVersion $actual, expected $expected"
            }
        }
    }
    finally
    {
        $zip.Dispose()
    }

    return [pscustomobject]@{
        Id         = $packageId
        Version    = $packageVersion
        Checked    = $checked
        Mismatches = $mismatches
        Unchecked  = $unchecked
        Required   = $required
        Missing    = @($missing | ForEach-Object { "  $packageId $packageVersion -> ${_}: absent" })
    }
}

function Invoke-Verify([string]$directory)
{
    $packages = @(Get-ChildItem -Path $directory -Filter '*.nupkg' -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($packages.Count -eq 0)
    {
        Write-Host "No .nupkg found in $directory — nothing was verified."
        return 1
    }

    # A rule table emptied by an edit would let every package through with nothing to say about it,
    # and the per-package check below cannot notice its own absence.
    if ($requiredPackageContent.Count -eq 0)
    {
        Write-Host "The required-content table is empty — no package content is being checked at all."
        return 1
    }

    $mismatches = @()
    $absent = @()
    $checked = 0
    $requiredCount = 0
    $matchedPackages = 0

    foreach ($package in $packages)
    {
        $result = Test-Package $package.FullName
        $checked += $result.Checked
        $mismatches += $result.Mismatches
        $absent += $result.Missing
        $requiredCount += $result.Required.Count
        if ($result.Required.Count -gt 0) { $matchedPackages++ }

        foreach ($skipped in $result.Unchecked)
        {
            Write-Host "  not checked: $skipped"
        }
    }

    # A floor catches a count reaching zero; it cannot catch a rule whose package id no longer names
    # any package, which reads as "nothing required" and passes. The tallies are what shows that.
    $tally = "required content: $( $requiredCount - $absent.Count ) of $requiredCount present ($($requiredPackageContent.Count) rule(s), $matchedPackages package(s) matched)"

    if ($mismatches.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packed assemblies do not carry their package's version:"
        $mismatches | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "These packages would install cleanly and fail at load: a consumer binds to the version"
        Write-Host "in the nuspec and the assembly answers to another. This is what release 0.11.1 shipped."
        Write-Host $tally
        return 1
    }

    if ($absent.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packages are missing content their id promises:"
        $absent | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "A Vion.Dale.Sdk without its analyzer assembly restores, compiles clean and judges nothing:"
        Write-Host "every consumer loses all forty-six diagnostics and the only signal is previously-red code"
        Write-Host "turning green. The pack condition is Exists(...), so a missed generator build packs this."
        Write-Host $tally
        return 1
    }

    Write-Host "Packed assembly versions: clean ($checked assemblies across $($packages.Count) packages)."
    Write-Host $tally
    return 0
}

function Invoke-SelfTest
{
    # A real managed assembly with a known AssemblyVersion, so the fixtures need no compiler. The
    # PowerShell host's own is always loaded and always on disk, whatever else the runner has.
    $sample = [System.Management.Automation.PSObject].Assembly.Location
    if (-not $sample -or -not (Test-Path $sample))
    {
        throw "Self-test needs a sample assembly on disk; the PowerShell host reports none."
    }

    $sampleName = [System.IO.Path]::GetFileNameWithoutExtension($sample)
    # Four parts, so the fixture works for a sample with a non-zero revision (7.4.6.500) too.
    $sampleVersion = [System.Reflection.AssemblyName]::GetAssemblyName($sample).Version

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("dale-gate-selftest-" + [Guid]::NewGuid().ToString('N'))
    $failures = @()

    try
    {
        # A package at a chosen id and version, carrying the sample assembly at each of the given zip
        # paths. Every copy answers to the sample's own AssemblyVersion, so a fixture built at
        # $matching is honest and one built at $stale is the 0.11.1 lie.
        function New-Fixture([string]$name, [string]$id, [string]$version, [string[]]$entries)
        {
            $directory = Join-Path $root $name
            $staging = Join-Path $directory 'staging'
            New-Item -ItemType Directory -Force -Path $staging | Out-Null

            Set-Content -Path (Join-Path $staging "$id.nuspec") -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$id</id>
    <version>$version</version>
  </metadata>
</package>
"@

            foreach ($entry in $entries)
            {
                $target = Join-Path $staging $entry
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                Copy-Item $sample $target
            }

            $nupkg = Join-Path $directory "$id.$version.nupkg"
            [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $nupkg)
            Remove-Item $staging -Recurse -Force
            return $directory
        }

        # Every case runs the script the way the CI job runs it — a child process over a directory of
        # .nupkg — so the parameter binding, the exit code and the printed report are all under test.
        # An in-process call to Invoke-Verify exercises none of the three.
        function Invoke-Gate([string]$directory)
        {
            $output = & pwsh -NoProfile -File $PSCommandPath -PackagesDir $directory 2>&1 | Out-String
            return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }

        $matching = $sampleVersion.ToString()
        $stale = "$($sampleVersion.Major + 1).0.0"
        $sdk = 'Vion.Dale.Sdk'
        $analyzer = 'analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll'

        # Expect is matched against the report as a whole. Every case that pins a tally has a peer
        # disagreeing with it: a number that has one value across the whole suite is a constant a
        # mutant can print.
        $cases = @(
            @{ Name = 'a package whose assembly carries the package version'
                Directory = (New-Fixture 'matching' $sampleName $matching @("lib/net10.0/$sampleName.dll"))
                Expected = 0
                Expect = @('clean (1 assemblies across 1 packages)',
                    'required content: 0 of 0 present (1 rule(s), 0 package(s) matched)') }
            @{ Name = 'the 0.11.1 shape — right nuspec version, stale assembly'
                Directory = (New-Fixture 'stale' $sampleName $stale @("lib/net10.0/$sampleName.dll"))
                Expected = 1
                Expect = @('do not carry their package', 'AssemblyVersion') }
            @{ Name = 'a package with no assemblies to judge'
                Directory = (New-Fixture 'empty' $sampleName $matching @())
                Expected = 0
                Expect = @('clean (0 assemblies across 1 packages)') }
            @{ Name = 'an empty package directory'
                Directory = (New-Item -ItemType Directory -Force -Path (Join-Path $root 'none')).FullName
                Expected = 1
                Expect = @('nothing was verified') }
            @{ Name = 'the SDK package carrying its analyzer'
                Directory = (New-Fixture 'sdk-with-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", $analyzer))
                Expected = 0
                Expect = @('required content: 1 of 1 present (1 rule(s), 1 package(s) matched)') }
            @{ Name = 'the SDK package packed without its analyzer'
                Directory = (New-Fixture 'sdk-without-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll"))
                Expected = 1
                Expect = @("$sdk $matching -> ${analyzer}: absent",
                    'required content: 0 of 1 present (1 rule(s), 1 package(s) matched)',
                    'judges nothing') }
            @{ Name = 'a package whose analyzer folder holds something else'
                Directory = (New-Fixture 'sdk-wrong-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", "analyzers/dotnet/cs/$sdk.Other.dll"))
                Expected = 1
                Expect = @("${analyzer}: absent") }
            @{ Name = 'a package whose analyzer path is spelled in the wrong case'
                Directory = (New-Fixture 'sdk-cased-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", "Analyzers/Dotnet/Cs/$sdk.Generators.dll"))
                Expected = 1
                Expect = @("${analyzer}: absent") }
        )

        foreach ($case in $cases)
        {
            $result = Invoke-Gate $case.Directory
            $unsaid = @($case.Expect | Where-Object { $result.Output -notlike "*$_*" })
            $passed = $result.ExitCode -eq $case.Expected -and $unsaid.Count -eq 0
            $verdict = if ($passed) { 'ok  ' } else { 'FAIL' }
            Write-Host "$verdict $($case.Name): exit $($result.ExitCode) (expected $($case.Expected))"
            foreach ($absent in $unsaid)
            {
                Write-Host "       the report does not say: $absent"
            }

            if (-not $passed)
            {
                $failures += $case.Name
            }
        }

        # The rule table is module state, so a fixture cannot vary it: a copy of this script with the
        # table rewritten is the only case shape that can. Two of them follow — the floor no fixture
        # reaches, and a second rule, because a tally that has one value across the whole suite is a
        # constant a mutant can print.
        function New-Variant([string]$name, [string[]]$body)
        {
            $lines = @(Get-Content -LiteralPath $PSCommandPath)
            $opening = [Array]::IndexOf($lines, '$requiredPackageContent = @{')
            $closing = [Array]::IndexOf($lines, '}', $opening)
            if ($opening -lt 0 -or $closing -lt 0)
            {
                throw "Self-test cannot find the rule table's own declaration to rewrite."
            }

            $path = Join-Path $root $name
            Set-Content -LiteralPath $path -Encoding UTF8 -Value (
                $lines[0..($opening - 1)] + $body + $lines[($closing + 1)..($lines.Count - 1)])
            return $path
        }

        $variants = @(
            @{ Name = 'an emptied rule table fails instead of reporting clean'
                Script = (New-Variant 'hollow.ps1' '$requiredPackageContent = @{}')
                Directory = $cases[0].Directory
                Expected = 1
                Expect = @('required-content table is empty') }
            @{ Name = 'a second rule is counted, not assumed'
                Script = (New-Variant 'two-rules.ps1' @(
                    '$requiredPackageContent = @{'
                    "    '$sdk' = @('$analyzer')"
                    "    'Vion.Dale.Sdk.Http' = @('lib/netstandard2.1/Vion.Dale.Sdk.Http.dll')"
                    '}'))
                Directory = ($cases | Where-Object { $_.Name -eq 'the SDK package carrying its analyzer' }).Directory
                Expected = 0
                Expect = @('required content: 1 of 1 present (2 rule(s), 1 package(s) matched)') }
        )

        foreach ($variant in $variants)
        {
            $output = & pwsh -NoProfile -File $variant.Script -PackagesDir $variant.Directory 2>&1 | Out-String
            $exit = $LASTEXITCODE
            $unsaid = @($variant.Expect | Where-Object { $output -notlike "*$_*" })
            if ($exit -eq $variant.Expected -and $unsaid.Count -eq 0)
            {
                Write-Host "ok   $($variant.Name)"
            }
            else
            {
                Write-Host "FAIL $($variant.Name): exit $exit (expected $($variant.Expected)), said: $($output.Trim())"
                $failures += $variant.Name
            }
        }

        # The rule table against the tree it is a rule about. A mis-typed package id matches no
        # package, so every fixture above still reports OK and no floor fires — the one mutation a
        # fixture cannot catch. This case reads the repository, so it holds wherever this self-test
        # runs: the verify-packages job of publish.yml, which a docs-only change skips.
        # Name comparison is -ceq throughout: a Windows file system answers Test-Path for
        # Vion.Dale.SDK, so a case built on Test-Path would catch a mis-typed id on the Linux runner
        # and pass at every desk.
        $repository = Split-Path $PSScriptRoot -Parent
        foreach ($id in $requiredPackageContent.Keys)
        {
            $folder = @(Get-ChildItem -LiteralPath $repository -Directory | Where-Object { $_.Name -ceq $id })
            $csproj = @($folder | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.csproj' } |
                    Where-Object { $_.Name -ceq "$id.csproj" })
            if ($csproj.Count -ne 1)
            {
                Write-Host "FAIL rule '$id' names no project in this repository"
                $failures += "rule '$id' names no project"
                continue
            }

            $text = Get-Content -LiteralPath $csproj[0].FullName -Raw
            foreach ($entry in $requiredPackageContent[$id])
            {
                $file = Split-Path $entry -Leaf
                $folder = (Split-Path $entry -Parent) -replace '/', '\\'
                if ($text -match [regex]::Escape($file) -and $text -match [regex]::Escape($folder))
                {
                    Write-Host "ok   rule '$id -> $entry' is content $id.csproj packs"
                }
                else
                {
                    Write-Host "FAIL rule '$id -> $entry' is not content $id.csproj packs"
                    $failures += "rule '$id -> $entry' is unpacked"
                }
            }
        }
    }
    finally
    {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host ""
    if ($failures.Count -gt 0)
    {
        Write-Host "Self-test FAILED: $($failures -join '; ')"
        return 1
    }

    Write-Host "Self-test passed."
    return 0
}

$exitCode = if ($SelfTest) { Invoke-SelfTest } else { Invoke-Verify $PackagesDir }
exit $exitCode
