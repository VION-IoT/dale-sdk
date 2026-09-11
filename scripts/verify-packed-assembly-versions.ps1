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

  It also fails any package carrying an analyzer assembly as a lib asset
  ($forbiddenLibAssemblies). Twelve packable projects reference Vion.Dale.Sdk.Generators
  with ReferenceOutputAssembly="false" OutputItemType="Analyzer"; drop that first attribute
  from any of them and the generator packs into lib/ as a reference the consumer compiles
  and loads against. It carries no Microsoft.CodeAnalysis dependency in the nuspec, so it
  is the 0.11.1 failure again — a well-formed package that dies at load. Nothing else sees
  it: for Vion.Dale.Sdk the generator's simple name is owned by the package id and its
  version is honest, and for the eleven siblings it lands in the unchecked bucket below,
  which reports without judging.

  It also fails a package that is missing content its id promises ($requiredPackageContent):
  today, a `Vion.Dale.Sdk` with no analyzer assembly. `build/Vion.Dale.Sdk.targets` is packed
  unconditionally and adds the analyzer unconditionally, while the analyzer itself is packed
  under Condition="Exists(...)" — so such a package breaks every consumer's build with
  CS0006 on a file the package itself promised. This names it in the release run instead.

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
# Condition="Exists(...)" (Vion.Dale.Sdk.csproj) while packing build/Vion.Dale.Sdk.targets, which
# references that analyzer, unconditionally — so a build that did not produce the assembly packs a
# package whose own targets file points at a file it does not carry. The gate is here because the
# artifact is the only place that shows before a consumer's build does.
$requiredPackageContent = @{
    'Vion.Dale.Sdk' = @('analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll')
}

# Assembly simple names that belong under analyzers/ and nowhere under lib/, whatever package they
# turn up in. An analyzer packed as a lib asset is a compile and runtime reference the consumer never
# asked for, and the pack that produces it is one deleted attribute away in each of the twelve
# packable projects that reference the generator. Matched case-insensitively: the pack writes the
# assembly's own name, so another casing is not the field shape, but a case-sensitive check is one a
# renamed output slips past for nothing gained.
$forbiddenLibAssemblies = @('Vion.Dale.Sdk.Generators')

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
    $analyzersInLib = @()
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

        # Entry names inside a .nupkg are zip paths, forward-slashed on every platform -- but a
        # PackagePath ending in a separator doubles it on the Linux runner, so the real artifact
        # carries `analyzers/dotnet/cs//Vion.Dale.Sdk.Generators.dll` (and every tools/ entry the same
        # way) while a package packed on Windows carries one slash. NuGet resolves either; this gate
        # refused the real one until its first CI run said so. Repeated separators are collapsed here.
        # The comparison is -cnotcontains because PowerShell's -contains is case-insensitive and the
        # convention path is not: a package carrying Analyzers/Dotnet/Cs/... restores and loads no
        # analyzer.
        $required = if ($requiredPackageContent.ContainsKey($packageId)) { @($requiredPackageContent[$packageId]) } else { @() }
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName -replace '/{2,}', '/' })
        $missing = @($required | Where-Object { $entryNames -cnotcontains $_ })

        foreach ($entry in $zip.Entries)
        {
            if ($entry.FullName -notlike 'lib/*' -and $entry.FullName -notlike 'analyzers/*') { continue }
            if ($entry.Name -notlike '*.dll') { continue }

            $simpleName = [System.IO.Path]::GetFileNameWithoutExtension($entry.Name)

            # Before ownership: for Vion.Dale.Sdk the generator IS owned by the package id, so a
            # check placed after the classification below would judge its version and say nothing.
            if ($entry.FullName -like 'lib/*' -and $forbiddenLibAssemblies -contains $simpleName)
            {
                $analyzersInLib += "  $packageId $packageVersion -> $($entry.FullName): an analyzer assembly, not a lib asset"
                continue
            }

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
        AnalyzersInLib = $analyzersInLib
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

    # Same floor for the other table: emptied, every package ships whatever it likes under lib/ and
    # the run still reports clean.
    if ($forbiddenLibAssemblies.Count -eq 0)
    {
        Write-Host "The forbidden-lib-assembly list is empty — no analyzer placement is being checked at all."
        return 1
    }

    $mismatches = @()
    $absent = @()
    $misplaced = @()
    $checked = 0
    $requiredCount = 0
    $matchedPackages = 0

    foreach ($package in $packages)
    {
        $result = Test-Package $package.FullName
        $checked += $result.Checked
        $mismatches += $result.Mismatches
        $absent += $result.Missing
        $misplaced += $result.AnalyzersInLib
        $requiredCount += $result.Required.Count
        if ($result.Required.Count -gt 0) { $matchedPackages++ }

        foreach ($skipped in $result.Unchecked)
        {
            Write-Host "  not checked: $skipped"
        }
    }

    $tally = "required content: $( $requiredCount - $absent.Count ) of $requiredCount present ($($requiredPackageContent.Count) rule(s), $matchedPackages package(s) matched)"
    $analyzerTally = "analyzer assemblies: $( if ($misplaced.Count -eq 0) { 'none' } else { $misplaced.Count } ) in lib/ ($($forbiddenLibAssemblies.Count) name(s) forbidden there)"

    # There is no floor on a rule matching no package: a directory holding one unrelated package is a
    # legitimate run. What that leaves — a rule id that names nothing anywhere — is a claim about this
    # repository, and the self-test checks it against this repository. The tally is what shows it here.

    if ($mismatches.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packed assemblies do not carry their package's version:"
        $mismatches | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "These packages would install cleanly and fail at load: a consumer binds to the version"
        Write-Host "in the nuspec and the assembly answers to another. This is what release 0.11.1 shipped."
        Write-Host $tally
        Write-Host $analyzerTally
        return 1
    }

    if ($absent.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packages are missing content their id promises:"
        $absent | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "A Vion.Dale.Sdk without its analyzer assembly still ships build/Vion.Dale.Sdk.targets, which"
        Write-Host "references that analyzer unconditionally — so every consumer's build fails with CS0006 on a"
        Write-Host "file this package promised. The analyzer's pack condition is Exists(...), so a missed"
        Write-Host "generator build produces exactly this."
        Write-Host $tally
        Write-Host $analyzerTally
        return 1
    }

    if ($misplaced.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packages carry an analyzer assembly as a lib asset:"
        $misplaced | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "An analyzer belongs under analyzers/dotnet/cs. Packed under lib/ it becomes a reference the"
        Write-Host "consumer compiles and loads against, with no Microsoft.CodeAnalysis dependency in the nuspec"
        Write-Host "to resolve it — a well-formed package that dies at load. A generator ProjectReference that"
        Write-Host "loses its ReferenceOutputAssembly=false attribute packs exactly this."
        Write-Host $tally
        Write-Host $analyzerTally
        return 1
    }

    Write-Host "Packed assembly versions: clean ($checked assemblies across $($packages.Count) packages)."
    Write-Host $tally
    Write-Host $analyzerTally
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
        function New-Fixture([string]$name, [string]$id, [string]$version, [string[]]$entries, [string[]]$notAssemblies, [string[]]$literalEntries)
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

            # Files that are named like assemblies and are not ones -- a native dll in lib/ is the real
            # shape. They reach Get-PackedAssemblyVersion and come back $null.
            foreach ($entry in $notAssemblies)
            {
                $target = Join-Path $staging $entry
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                Set-Content -LiteralPath $target -Encoding UTF8 -Value 'not a PE image'
            }

            $nupkg = Join-Path $directory "$id.$version.nupkg"
            [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $nupkg)
            Remove-Item $staging -Recurse -Force

            # Entry names written verbatim, because CreateFromDirectory normalises separators and the
            # shape this gate meets in production does not: `dotnet pack` on Linux doubles the one a
            # PackagePath ends with.
            if ($literalEntries)
            {
                $archive = [System.IO.Compression.ZipFile]::Open($nupkg, [System.IO.Compression.ZipArchiveMode]::Update)
                try
                {
                    $bytes = [System.IO.File]::ReadAllBytes($sample)
                    foreach ($literal in $literalEntries)
                    {
                        $stream = $archive.CreateEntry($literal).Open()
                        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
                    }
                }
                finally
                {
                    $archive.Dispose()
                }
            }

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
        # Sorts after $sdk, as its real sibling does: the accumulator mutants only show when the
        # package carrying the finding is not the last one judged.
        $sibling = 'Vion.Dale.Sdk.TestKit'
        $http = 'Vion.Dale.Sdk.Http'
        $analyzer = 'analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll'

        # Two packages in one directory, the sibling written first so the id under a rule is not the
        # last one judged.
        New-Fixture 'pair-without-analyzer' $sibling $matching @("lib/net10.0/$sibling.dll") | Out-Null
        $pairWithoutAnalyzer = New-Fixture 'pair-without-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll")
        New-Fixture 'pair-with-analyzer' $sibling $matching @("lib/net10.0/$sibling.dll") | Out-Null
        $pairWithAnalyzer = New-Fixture 'pair-with-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", $analyzer)

        # A stale package that is NOT the last one judged. Every other fixture is honest about its
        # versions, so the accumulator carrying the defect this whole script exists for -- the 0.11.1
        # shape -- was the one still reading the last package's findings rather than the run's.
        New-Fixture 'pair-stale-first' $sibling $matching @("lib/net10.0/$sibling.dll") | Out-Null
        $pairStaleFirst = New-Fixture 'pair-stale-first' $sdk $stale @("lib/netstandard2.1/$sdk.dll", $analyzer)

        # Both packages under a rule, for the two-rule variant: with one, `$matchedPackages++` and
        # `$matchedPackages = 1` are the same number.
        New-Fixture 'pair-two-rules' $http $matching @("lib/netstandard2.1/$http.dll") | Out-Null
        $pairTwoRules = New-Fixture 'pair-two-rules' $sdk $matching @("lib/netstandard2.1/$sdk.dll", $analyzer)

        # The analyzer assembly shipped as a lib asset: what dropping ReferenceOutputAssembly="false"
        # from any of the twelve packable projects that reference the generator produces. The sibling
        # is written first again, so the finding has to survive the packages judged after it.
        $generator = "$sdk.Generators.dll"
        New-Fixture 'pair-analyzer-in-lib' $sibling $matching @("lib/net10.0/$sibling.dll") | Out-Null
        $pairAnalyzerInLib = New-Fixture 'pair-analyzer-in-lib' $sdk $matching @(
            "lib/netstandard2.1/$sdk.dll", $analyzer, "lib/netstandard2.1/$generator")

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
                Expect = @('do not carry their package', 'AssemblyVersion',
                    'required content: 0 of 0 present (1 rule(s), 0 package(s) matched)') }
            @{ Name = 'a package with no assemblies to judge'
                Directory = (New-Fixture 'empty' $sampleName $matching @())
                Expected = 0
                Expect = @('clean (0 assemblies across 1 packages)') }
            # The docstring's "listed rather than trusted silently": two of them, because one foreign
            # assembly cannot tell a list from a variable holding the last one.
            @{ Name = 'foreign assemblies are listed, not judged'
                Directory = (New-Fixture 'foreign' $sampleName $matching @('lib/net10.0/Foreign.One.dll', 'lib/net10.0/Foreign.Two.dll'))
                Expected = 0
                Expect = @('not checked: lib/net10.0/Foreign.One.dll (not an assembly of',
                    'not checked: lib/net10.0/Foreign.Two.dll (not an assembly of',
                    'clean (0 assemblies across 1 packages)') }
            @{ Name = 'files that are named like assemblies and are not ones'
                Directory = (New-Fixture 'native' $sampleName $matching @("lib/net10.0/$sampleName.dll") `
                        @("lib/net10.0/$sampleName.Native.dll", "lib/net10.0/$sampleName.Other.dll"))
                Expected = 0
                Expect = @("not checked: lib/net10.0/$sampleName.Native.dll (not a managed assembly)",
                    "not checked: lib/net10.0/$sampleName.Other.dll (not a managed assembly)",
                    'clean (1 assemblies across 1 packages)') }
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
                    'fails with CS0006') }
            @{ Name = 'a package whose analyzer folder holds something else'
                Directory = (New-Fixture 'sdk-wrong-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", "analyzers/dotnet/cs/$sdk.Other.dll"))
                Expected = 1
                Expect = @("${analyzer}: absent") }
            @{ Name = 'the analyzer at the doubled separator dotnet pack writes on Linux'
                Directory = (New-Fixture 'sdk-doubled-separator' $sdk $matching @("lib/netstandard2.1/$sdk.dll") @() `
                        @("analyzers/dotnet/cs//$sdk.Generators.dll"))
                Expected = 0
                Expect = @('required content: 1 of 1 present (1 rule(s), 1 package(s) matched)') }
            @{ Name = 'a package whose analyzer path is spelled in the wrong case'
                Directory = (New-Fixture 'sdk-cased-analyzer' $sdk $matching @("lib/netstandard2.1/$sdk.dll", "Analyzers/Dotnet/Cs/$sdk.Generators.dll"))
                Expected = 1
                Expect = @("${analyzer}: absent") }
            # An analyzer assembly under lib/ is judged whoever the package is: for Vion.Dale.Sdk the
            # simple name is owned by the package id and passes the version check, for the eleven
            # siblings it lands in the unchecked bucket and is reported without a verdict. Neither
            # says anything, which is the whole finding.
            @{ Name = 'the SDK package shipping its analyzer as a lib asset'
                Directory = (New-Fixture 'sdk-analyzer-in-lib' $sdk $matching @("lib/netstandard2.1/$sdk.dll", $analyzer, "lib/netstandard2.1/$sdk.Generators.dll"))
                Expected = 1
                Expect = @("$sdk $matching -> lib/netstandard2.1/$sdk.Generators.dll: an analyzer assembly, not a lib asset",
                    'analyzer assemblies: 1 in lib/ (1 name(s) forbidden there)') }
            @{ Name = 'a sibling package shipping the analyzer as a lib asset'
                Directory = (New-Fixture 'http-analyzer-in-lib' $http $matching @("lib/netstandard2.1/$http.dll", "lib/netstandard2.1/$sdk.Generators.dll"))
                Expected = 1
                Expect = @("$http $matching -> lib/netstandard2.1/$sdk.Generators.dll: an analyzer assembly, not a lib asset") }
            # The pack that produces this writes the assembly's own name, so an upper-cased entry is
            # not the shape in the field -- but a check that reads the name case-sensitively is one a
            # renamed output slips past for free, and nothing is gained by letting it.
            @{ Name = 'an analyzer in lib/ spelled in another case is still caught'
                Directory = (New-Fixture 'sdk-cased-analyzer-in-lib' $sdk $matching @("lib/netstandard2.1/$sdk.dll", $analyzer, "lib/netstandard2.1/VION.DALE.SDK.GENERATORS.dll"))
                Expected = 1
                Expect = @('an analyzer assembly, not a lib asset') }
            @{ Name = 'the analyzer under analyzers/ is where it belongs'
                Directory = $pairWithAnalyzer
                Expected = 0
                Expect = @('analyzer assemblies: none in lib/ (1 name(s) forbidden there)') }
            # A release artifact set is many packages and Vion.Dale.Sdk never sorts last in it, so the
            # findings have to survive the packages judged after it. Both of these run on a directory
            # holding two, which is what pins the three cross-package accumulators; every case above
            # holds one, where "the total" and "the last package's" are the same number.
            @{ Name = 'a missing entry survives the packages judged after it'
                Directory = $pairWithoutAnalyzer
                Expected = 1
                Expect = @("$sdk $matching -> ${analyzer}: absent",
                    'required content: 0 of 1 present (1 rule(s), 1 package(s) matched)') }
            @{ Name = 'a stale assembly survives the packages judged after it'
                Directory = $pairStaleFirst
                Expected = 1
                Expect = @("$sdk $stale -> lib/netstandard2.1/$sdk.dll: AssemblyVersion",
                    'do not carry their package',
                    'required content: 1 of 1 present (1 rule(s), 1 package(s) matched)') }
            @{ Name = 'an analyzer in lib/ survives the packages judged after it'
                Directory = $pairAnalyzerInLib
                Expected = 1
                Expect = @("$sdk $matching -> lib/netstandard2.1/$sdk.Generators.dll: an analyzer assembly, not a lib asset",
                    'analyzer assemblies: 1 in lib/ (1 name(s) forbidden there)') }
            @{ Name = 'two packages, one of them carrying its required content'
                Directory = $pairWithAnalyzer
                Expected = 0
                Expect = @('clean (3 assemblies across 2 packages)',
                    'required content: 1 of 1 present (1 rule(s), 1 package(s) matched)') }
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
        function New-Variant([string]$name, [string]$variable, [string[]]$body)
        {
            $lines = @(Get-Content -LiteralPath $PSCommandPath)
            $opening = [Array]::FindIndex($lines, [Predicate[string]]{ param($line) $line.StartsWith("$variable = ") })
            # A table opens a brace and runs to the next line that is one alone; a list is declared and
            # closed on its own line.
            $closing = if ($opening -ge 0 -and $lines[$opening].EndsWith('@{')) { [Array]::IndexOf($lines, '}', $opening) } else { $opening }
            if ($opening -lt 0 -or $closing -lt 0)
            {
                throw "Self-test cannot find $variable's own declaration to rewrite."
            }

            $path = Join-Path $root $name
            Set-Content -LiteralPath $path -Encoding UTF8 -Value (
                $lines[0..($opening - 1)] + $body + $lines[($closing + 1)..($lines.Count - 1)])
            return $path
        }

        $variants = @(
            @{ Name = 'an emptied rule table fails instead of reporting clean'
                Script = (New-Variant 'hollow.ps1' '$requiredPackageContent' '$requiredPackageContent = @{}')
                Directory = $cases[0].Directory
                Expected = 1
                Expect = @('required-content table is empty') }
            @{ Name = 'a second rule is counted, not assumed'
                Script = (New-Variant 'two-rules.ps1' '$requiredPackageContent' @(
                    '$requiredPackageContent = @{'
                    "    '$sdk' = @('$analyzer')"
                    "    '$http' = @('lib/netstandard2.1/$http.dll')"
                    '}'))
                Directory = $pairTwoRules
                Expected = 0
                Expect = @('required content: 2 of 2 present (2 rule(s), 2 package(s) matched)') }
        )

        $variants += @(
            @{ Name = 'an emptied forbidden-list fails instead of reporting clean'
                Script = (New-Variant 'no-forbidden.ps1' '$forbiddenLibAssemblies' '$forbiddenLibAssemblies = @()')
                Directory = $cases[0].Directory
                Expected = 1
                Expect = @('forbidden-lib-assembly list is empty') }
            # A second forbidden name, for the same reason the two-rule variant exists: with one, the
            # count in the tally and the literal 1 are the same number.
            @{ Name = 'a second forbidden name is counted, not assumed'
                Script = (New-Variant 'two-forbidden.ps1' '$forbiddenLibAssemblies' "`$forbiddenLibAssemblies = @('$sdk.Generators', 'Some.Other.Analyzer')")
                Directory = $pairWithAnalyzer
                Expected = 0
                Expect = @('analyzer assemblies: none in lib/ (2 name(s) forbidden there)') }
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
            $projectDirectory = @(Get-ChildItem -LiteralPath $repository -Directory | Where-Object { $_.Name -ceq $id })
            $csproj = @($projectDirectory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.csproj' } |
                    Where-Object { $_.Name -ceq "$id.csproj" })
            if ($csproj.Count -ne 1)
            {
                Write-Host "FAIL rule '$id' names no project in this repository"
                $failures += "rule '$id' names no project"
                continue
            }

            $text = Get-Content -LiteralPath $csproj[0].FullName -Raw

            # The rule is keyed on the PACKAGE id, and a project may declare one that is not its
            # name. Absent, MSBuild defaults PackageId to AssemblyName to the project name, which is
            # what the rule key already matched above.
            $declaredId = ([regex]::Match($text, '<PackageId>([^<]*)</PackageId>')).Groups[1].Value
            if ($declaredId -and $declaredId -cne $id)
            {
                Write-Host "FAIL rule '$id' is a project name; the package it produces is '$declaredId'"
                $failures += "rule '$id' names a project whose package id differs"
                continue
            }

            # A required entry is a ZIP path, always forward-slashed, and the csproj writes the same
            # place with backslashes. Both sides are normalised to '/' here and nothing asks the
            # platform: the first version of this case went through Split-Path, passed on Windows and
            # failed on the Linux runner. Which of its two uses diverged is not recorded, because the
            # fix is not to find out -- a zip path is not a filesystem path and never needed a
            # filesystem API. Split-Path does answer in backslashes on Windows whatever separator it
            # is given (measured), so a comparison built on it cannot be platform-neutral by accident.
            $packed = $text -replace '\\', '/'
            foreach ($entry in $requiredPackageContent[$id])
            {
                $cut = $entry.LastIndexOf('/')
                $file = $entry.Substring($cut + 1)
                $folder = $entry.Substring(0, $cut)
                if ($packed -match [regex]::Escape($file) -and $packed -match [regex]::Escape($folder))
                {
                    Write-Host "ok   rule '$id -> $entry' is content $id.csproj packs"
                }
                else
                {
                    Write-Host "FAIL rule '$id -> $entry' is not content $id.csproj packs"
                    Write-Host "       looked for '$file' and '$folder' in $id.csproj, slashes normalised"
                    $failures += "rule '$id -> $entry' is unpacked"
                }
            }
        }

        # The forbidden names against the tree they are names about, for the same reason the rule ids
        # above are checked: a mis-typed name matches no entry, so every fixture still reports OK and
        # no floor fires. -ceq again, because a Windows file system answers Test-Path for
        # Vion.Dale.Sdk.GENERATORS and the Linux runner does not.
        foreach ($name in $forbiddenLibAssemblies)
        {
            $projectDirectory = @(Get-ChildItem -LiteralPath $repository -Directory | Where-Object { $_.Name -ceq $name })
            $csproj = @($projectDirectory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.csproj' } |
                    Where-Object { $_.Name -ceq "$name.csproj" })
            if ($csproj.Count -eq 1)
            {
                Write-Host "ok   forbidden name '$name' is an assembly this repository builds"
            }
            else
            {
                Write-Host "FAIL forbidden name '$name' is no assembly this repository builds"
                $failures += "forbidden name '$name' names no project"
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
