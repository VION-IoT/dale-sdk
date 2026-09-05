#requires -Version 7
# Self-test for set-version.ps1's package-id roster: every packable project's <PackageId> must be
# named in $sdkPackageIds, because Clear-NuGetPackageCache clears exactly that list after a release
# and a package it does not name keeps its previous version in the local cache. Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/set-version.tests.ps1`.
#
# Scope: the roster only. The version-bumping itself is still out of a fast self-test's reach (it
# rewrites csprojs across the tree), which is what set-version.ps1's entry in run-script-tests.ps1's
# $exempt list used to cover for the whole script.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$script = Join-Path $PSScriptRoot 'set-version.ps1'

function Get-RosterIds([string]$path) {
    $text = Get-Content -LiteralPath $path -Raw
    $block = [regex]::Match($text, '\$sdkPackageIds\s*=\s*@\((?<body>[^)]*)\)')
    if (-not $block.Success) { throw 'could not find $sdkPackageIds in set-version.ps1' }
    return [regex]::Matches($block.Groups['body'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
}

# Packable is MSBuild's default, so a project is packable unless something turns it off: an explicit
# <IsPackable>false</IsPackable> (matched attribute-tolerantly, because the bundled template writes
# <IsPackable Condition="true">false</IsPackable>), or the MSTest / Microsoft.NET.Test.Sdk props,
# which set IsPackable=false off IsTestProject. A filter reading <IsPackable>true</IsPackable> instead
# cannot see a project that declares neither — Vion.Dale.Cli is packed and published like every other
# id and was invisible to this check while it read the positive form.
function Get-PackableIds([string]$root) {
    Get-ChildItem -LiteralPath $root -Filter '*.csproj' -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            if ($text -match '<IsPackable[^>]*>\s*false\s*</IsPackable>') { return }
            if ($text -match '<IsTestProject[^>]*>\s*true\s*</IsTestProject>' -or
                $text -match 'PackageReference\s+Include="(MSTest|Microsoft\.NET\.Test\.Sdk)"') { return }

            # A packable project declaring no <PackageId> ships under its assembly name. Name it rather
            # than drop it, or the filter goes blind again the next time one is added.
            if ($text -match '<PackageId>([^<]+)</PackageId>') { $Matches[1] } else { $_.BaseName }
        }
}

try {
    # Case 1: the real roster names every packable PackageId in the repository.
    $roster = @(Get-RosterIds $script)
    $packable = @(Get-PackableIds $repoRoot | Sort-Object -Unique)
    if ($packable.Count -lt 10) { throw "Case 1 expected to find the repository's packable projects, found $($packable.Count)" }
    $missing = @($packable | Where-Object { $roster -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Case 1 (roster covers every packable PackageId) — missing from `$sdkPackageIds: $($missing -join ', ')"
    }

    # Case 2: the roster names nothing that is not a packable PackageId, so a renamed or retired
    # package leaves a stale entry behind rather than passing unnoticed.
    $stale = @($roster | Where-Object { $packable -notcontains $_ })
    if ($stale.Count -gt 0) {
        throw "Case 2 (roster names only packable PackageIds) — stale entries: $($stale -join ', ')"
    }

    # Case 3: the check can fail. A roster with one id removed must be detected as incomplete, or
    # Cases 1 and 2 would pass on a comparison that compares nothing.
    $mutated = @($roster | Select-Object -Skip 1)
    $wouldMiss = @($packable | Where-Object { $mutated -notcontains $_ })
    if ($wouldMiss.Count -ne 1) {
        throw "Case 3 (the comparison can fail) expected exactly 1 missing id after dropping one, got $($wouldMiss.Count)"
    }

    # Case 4: the filter itself can see a project that is packable by MSBuild's default. Cases 1-3
    # only compare two sets; a filter blind to a whole class of projects passes all three while the
    # roster is incomplete, which is how Vion.Dale.Cli stayed missing. Reading <IsPackable>true</…>
    # is that blindness, so the widened filter must find ids the narrow one does not.
    $narrow = @(Get-ChildItem -LiteralPath $repoRoot -Filter '*.csproj' -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            if ($text -match '<IsPackable>\s*true\s*</IsPackable>' -and $text -match '<PackageId>([^<]+)</PackageId>') { $Matches[1] }
        } | Sort-Object -Unique)
    $defaulted = @($packable | Where-Object { $narrow -notcontains $_ })
    if ($defaulted.Count -lt 1) {
        throw "Case 4 (the filter sees a package that is packable by default) — the filter found nothing an <IsPackable>true</IsPackable> match would have missed, so it has narrowed back"
    }

    Write-Host "set-version.tests: PASS ($($packable.Count) packable package id(s), all in the roster; $($defaulted.Count) packable by default: $($defaulted -join ', '))"
    exit 0
}
catch {
    Write-Host "set-version.tests: FAIL - $_"
    exit 1
}
