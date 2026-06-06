#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Stage each package's XML doc file into the bin/Release layout the api-reference
  generator globs — so drift-and-docs can skip rebuilding the whole solution.

.DESCRIPTION
  scripts/generate-api-reference.cjs reads <project>/bin/Release/**/<Assembly>.xml.
  Those XML files already ship inside the packed .nupkgs (as lib/<tfm>/<Assembly>.xml),
  so extract them instead of recompiling on the docs runner. Project directory ==
  assembly name (repo convention), so each <Assembly>.xml lands at
  ./<Assembly>/bin/Release/frompkg/<Assembly>.xml, which the generator's
  bin/Release/** glob then finds. Uses System.IO.Compression so it runs the same on
  Windows and the Linux CI runner.

.PARAMETER PackagesDir
  Directory containing the .nupkg files (e.g. ./artifacts).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagesDir
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$count = 0
foreach ($nupkg in Get-ChildItem -Path "$PackagesDir/*.nupkg")
{
    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try
    {
        foreach ($entry in $zip.Entries)
        {
            if ($entry.FullName -like 'lib/*/*.xml')
            {
                $asm = [System.IO.Path]::GetFileNameWithoutExtension($entry.Name)
                $dest = "./$asm/bin/Release/frompkg"
                New-Item -ItemType Directory -Force -Path $dest | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$dest/$asm.xml", $true)
                $count++
            }
        }
    }
    finally
    {
        $zip.Dispose()
    }
}

Write-Host "Staged $count XML doc file(s) from $PackagesDir."
