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

    $mismatches = @()
    $checked = 0

    foreach ($package in $packages)
    {
        $result = Test-Package $package.FullName
        $checked += $result.Checked
        $mismatches += $result.Mismatches

        foreach ($skipped in $result.Unchecked)
        {
            Write-Host "  not checked: $skipped"
        }
    }

    if ($mismatches.Count -gt 0)
    {
        Write-Host ""
        Write-Host "Packed assemblies do not carry their package's version:"
        $mismatches | ForEach-Object { Write-Host $_ }
        Write-Host ""
        Write-Host "These packages would install cleanly and fail at load: a consumer binds to the version"
        Write-Host "in the nuspec and the assembly answers to another. This is what release 0.11.1 shipped."
        return 1
    }

    Write-Host "Packed assembly versions: clean ($checked assemblies across $($packages.Count) packages)."
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
        # A package whose id is the sample assembly's name, at the version the sample carries or at
        # another one. Only the second is a lie, and the gate has to tell them apart.
        function New-Fixture([string]$name, [string]$version, [bool]$withAssembly)
        {
            $directory = Join-Path $root $name
            $staging = Join-Path $directory 'staging'
            New-Item -ItemType Directory -Force -Path (Join-Path $staging 'lib/net10.0') | Out-Null

            Set-Content -Path (Join-Path $staging "$sampleName.nuspec") -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$sampleName</id>
    <version>$version</version>
  </metadata>
</package>
"@

            if ($withAssembly)
            {
                Copy-Item $sample (Join-Path $staging "lib/net10.0/$sampleName.dll")
            }

            $nupkg = Join-Path $directory "$sampleName.$version.nupkg"
            [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $nupkg)
            Remove-Item $staging -Recurse -Force
            return $directory
        }

        $matching = $sampleVersion.ToString()
        $stale = "$($sampleVersion.Major + 1).0.0"

        $cases = @(
            @{ Name = 'a package whose assembly carries the package version'; Directory = (New-Fixture 'matching' $matching $true); Expected = 0 }
            @{ Name = 'the 0.11.1 shape — right nuspec version, stale assembly'; Directory = (New-Fixture 'stale' $stale $true); Expected = 1 }
            @{ Name = 'a package with no assemblies to judge'; Directory = (New-Fixture 'empty' $matching $false); Expected = 0 }
            @{ Name = 'an empty package directory'; Directory = (New-Item -ItemType Directory -Force -Path (Join-Path $root 'none')).FullName; Expected = 1 }
        )

        foreach ($case in $cases)
        {
            $actual = Invoke-Verify $case.Directory
            $verdict = if ($actual -eq $case.Expected) { 'ok  ' } else { 'FAIL' }
            Write-Host "$verdict $($case.Name): exit $actual (expected $($case.Expected))"
            if ($actual -ne $case.Expected)
            {
                $failures += $case.Name
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
