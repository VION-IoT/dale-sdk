#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fail when a package of this repository is not binary-compatible with the same package in the
  previous release.

.DESCRIPTION
  A logic-block library declares the oldest Vion.Dale.Sdk it runs on, and a host runtime warns when it
  is older than that. A minimum is a sufficient answer only while every release is additive over the
  one before it within a major: a member removed or changed in 0.17.0 breaks a library built against
  0.16.0 on a 0.17.0 host, whatever minimum it declared. This is the gate that keeps it additive.

  It packs the whole solution with the .NET SDK's package validation against a baseline. The baseline
  is the previous release: the highest vMAJOR.MINOR.PATCH tag reachable from HEAD that does not point
  at HEAD itself, so a release tag's own run compares against its predecessor and never against the
  package it is packing. Prerelease tags are not releases and are passed over. Each packable library
  project validates itself once a baseline is given (Directory.Build.targets); the CLI tool does not.

  There is no suppression file. A finding is fixed in the code — an addition instead of a change, a
  default implementation on a new interface member — or it waits for the next major.

  The pack runs at a version just above the baseline, because the validation also refuses an assembly
  version below the baseline's, and every ordinary build of this repository stamps 0.0.0. It deletes
  the validation's up-to-date markers first: the validation is skipped when its inputs are unchanged
  since the last run, whatever the baseline was then. Its packages go to a temporary directory and are
  deleted; they are never the release.

  The tags must be present. A shallow clone without them has no baseline, and that fails rather than
  passing as nothing to compare.

.PARAMETER Baseline
  Compare against this MAJOR.MINOR.PATCH release instead of deriving it from the tags.

.PARAMETER RepoRoot
  The repository to validate; defaults to the one this script lives in.

.PARAMETER SelfTest
  Verify the baseline derivation against throwaway git repositories, without packing anything.

.EXAMPLE
  pwsh scripts/validate-packages.ps1
  The CI gate: exit 1 on any binary-incompatible change from the previous release.

.EXAMPLE
  pwsh scripts/validate-packages.ps1 -Baseline 0.14.1
  Compare against an older release, at the desk.
#>
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(ParameterSetName = 'Validate')]
    [string]$Baseline,

    [Parameter(ParameterSetName = 'Validate')]
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),

    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$releaseTag = '^v(\d+)\.(\d+)\.(\d+)$'

# The highest release tag reachable from HEAD that does not point at HEAD, as MAJOR.MINOR.PATCH, or
# $null when there is none. Ordered by version, never by name or by date: by name, v0.9.0 sorts after
# v0.10.0, and the most recently created tag need not be the highest.
function Get-ValidationBaseline([string]$repository)
{
    $atHead = @(git -C $repository tag --points-at HEAD)
    if ($LASTEXITCODE -ne 0) { throw "git tag --points-at HEAD failed in $repository" }

    $reachable = @(git -C $repository tag --merged HEAD)
    if ($LASTEXITCODE -ne 0) { throw "git tag --merged HEAD failed in $repository" }

    $versions = @($reachable | Where-Object { $_ -match $releaseTag -and $_ -notin $atHead } | ForEach-Object { [Version]$_.Substring(1) })
    if ($versions.Count -eq 0)
    {
        return $null
    }

    return ($versions | Sort-Object -Descending | Select-Object -First 1).ToString(3)
}

# The projects of $projectPaths (repository-relative, forward slashes) that do not exist in the tree
# of $baselineTag. Each ships its first release with this one: there is no baseline package to compare
# it with, and asking restore for one fails the whole pack.
function Get-ProjectsWithoutBaseline([string]$repository, [string]$baselineTag, [string[]]$projectPaths)
{
    git -C $repository rev-parse --verify --quiet "$baselineTag^{commit}" *> $null
    if ($LASTEXITCODE -ne 0) { throw "the baseline tag $baselineTag does not exist in $repository; fetch the tags (git fetch --tags)" }

    return @($projectPaths | Where-Object {
            git -C $repository cat-file -e "$($baselineTag):$_" *> $null
            $LASTEXITCODE -ne 0
        } | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })
}

# The projects Vion.Dale.Sdk.sln builds, repository-relative with forward slashes.
function Get-SolutionProjects([string]$repository)
{
    return @([regex]::Matches((Get-Content -Raw -LiteralPath (Join-Path $repository 'Vion.Dale.Sdk.sln')), '"([^"]+\.csproj)"') |
            ForEach-Object { $_.Groups[1].Value.Replace('\', '/') })
}

# The version the validation pack stamps: one patch above the baseline, marked as no release.
function Get-ValidationVersion([string]$baseline)
{
    $version = [Version]$baseline
    return "$($version.Major).$($version.Minor).$($version.Build + 1)-validation"
}

function Invoke-Validate
{
    $baselineVersion = if ($Baseline) { $Baseline } else { Get-ValidationBaseline $RepoRoot }
    if (-not $baselineVersion)
    {
        Write-Host "validate-packages: FAIL - no vMAJOR.MINOR.PATCH tag is reachable from HEAD in $RepoRoot other than one on HEAD itself, so there is no previous release to compare against. A shallow clone carries no tags: fetch them (git fetch --tags, or fetch-depth: 0)."
        return 1
    }

    if ($baselineVersion -notmatch '^\d+\.\d+\.\d+$')
    {
        Write-Host "validate-packages: FAIL - baseline '$baselineVersion' is not MAJOR.MINOR.PATCH."
        return 1
    }

    $version = Get-ValidationVersion $baselineVersion
    Write-Host "validate-packages: packing at $version against the $baselineVersion release"

    $withoutBaseline = Get-ProjectsWithoutBaseline $RepoRoot "v$baselineVersion" (Get-SolutionProjects $RepoRoot)
    if ($withoutBaseline.Count -gt 0)
    {
        Write-Host "validate-packages: not in the $baselineVersion release, so nothing to compare: $($withoutBaseline -join ', ')"
    }

    Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Filter 'Microsoft.NET.ApiCompat.ValidatePackage.semaphore' |
        Where-Object { $_.FullName -match '[\\/]obj[\\/]' } |
        Remove-Item -Force

    $output = Join-Path ([System.IO.Path]::GetTempPath()) ("dale-validate-packages-" + [Guid]::NewGuid().ToString('N'))
    try
    {
        # To the host, not the pipeline: anything this function writes to the pipeline joins its return
        # value, and the exit code below would then be the pack's output instead of 0 or 1.
        & dotnet pack (Join-Path $RepoRoot 'Vion.Dale.Sdk.sln') -c Release "-p:Version=$version" "-p:PackageValidationBaselineVersion=$baselineVersion" "-p:DaleProjectsWithoutBaseline=$($withoutBaseline -join '|')" -o $output | Out-Host
        $packExit = $LASTEXITCODE
        $packages = @(Get-ChildItem -LiteralPath $output -Filter '*.nupkg' -ErrorAction SilentlyContinue)
    }
    finally
    {
        Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($packExit -ne 0)
    {
        Write-Host "validate-packages: FAIL - the pack against $baselineVersion failed; a CP diagnostic above names each binary-incompatible change. Fix it in the code, or it waits for the next major."
        return 1
    }

    if ($packages.Count -eq 0)
    {
        Write-Host "validate-packages: FAIL - the pack produced no package, so nothing was validated."
        return 1
    }

    Write-Host "validate-packages: OK - $($packages.Count) package(s) packed against the $baselineVersion release, every library package binary-compatible with it"
    return 0
}

function Invoke-SelfTest
{
    if (-not (Get-Command git -ErrorAction SilentlyContinue))
    {
        Write-Host 'validate-packages self-test: SKIPPED (git not on PATH)'
        return 0
    }

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("dale-validate-selftest-" + [Guid]::NewGuid().ToString('N'))
    $failures = [System.Collections.Generic.List[string]]::new()

    function Invoke-Git([string[]]$arguments)
    {
        & git -C $repository @arguments 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $($arguments -join ' ') failed in the self-test fixture" }
    }

    function New-Commit([string]$tag)
    {
        Invoke-Git @('commit', '--allow-empty', '--quiet', '-m', [Guid]::NewGuid().ToString('N'))
        if ($tag) { Invoke-Git @('tag', $tag) }
    }

    function Assert-Baseline([string]$case, [string]$expected)
    {
        $actual = Get-ValidationBaseline $repository
        if ("$actual" -eq "$expected")
        {
            Write-Host "ok   $case -> $(if ($expected) { $expected } else { 'none' })"
        }
        else
        {
            Write-Host "FAIL $case -> expected '$expected', derived '$actual'"
            $failures.Add($case)
        }
    }

    try
    {
        $repository = Join-Path $root 'repo'
        New-Item -ItemType Directory -Path $repository | Out-Null
        Invoke-Git @('init', '--quiet', '--initial-branch=main')
        Invoke-Git @('config', 'user.name', 'self-test')
        Invoke-Git @('config', 'user.email', 'self-test@example.invalid')
        Invoke-Git @('config', 'commit.gpgsign', 'false')

        New-Commit $null
        Assert-Baseline 'no release tag at all' $null

        # v0.9.0 is tagged after v0.10.0 and sorts after it by name, so neither the newest tag nor the
        # last name is the highest version.
        New-Commit 'v0.10.0'
        New-Commit 'v0.9.0'
        New-Commit $null
        Assert-Baseline 'highest by version, not by name or by date' '0.10.0'

        Invoke-Git @('tag', 'v0.11.0')
        Assert-Baseline 'a tag on HEAD is the package being packed, not its baseline' '0.10.0'

        New-Commit $null
        Assert-Baseline 'a tag below HEAD is the previous release' '0.11.0'

        New-Commit 'v0.12.0-rc.1'
        New-Commit 'nightly'
        New-Commit $null
        Assert-Baseline 'a prerelease or non-version tag is not a release' '0.11.0'

        Invoke-Git @('checkout', '--quiet', '-b', 'side', 'v0.10.0')
        New-Commit 'v0.99.0'
        Invoke-Git @('checkout', '--quiet', 'main')
        Assert-Baseline 'a tag not reachable from HEAD is not this line''s release' '0.11.0'

        # A project added after the baseline release ships its first release now: nothing to compare.
        Invoke-Git @('checkout', '--quiet', 'v0.10.0')
        New-Item -ItemType Directory -Path (Join-Path $repository 'Old') | Out-Null
        Set-Content -LiteralPath (Join-Path $repository 'Old/Old.csproj') -Value '<Project />'
        Invoke-Git @('checkout', '--quiet', '-b', 'projects')
        Invoke-Git @('add', 'Old/Old.csproj')
        New-Commit 'v0.20.0'
        New-Item -ItemType Directory -Path (Join-Path $repository 'New') | Out-Null
        Set-Content -LiteralPath (Join-Path $repository 'New/New.csproj') -Value '<Project />'
        Invoke-Git @('add', 'New/New.csproj')
        New-Commit $null
        $missing = @(Get-ProjectsWithoutBaseline $repository 'v0.20.0' @('Old/Old.csproj', 'New/New.csproj'))
        if (($missing -join ',') -eq 'New') { Write-Host 'ok   a project absent from the baseline release -> New' }
        else { Write-Host "FAIL a project absent from the baseline release -> '$($missing -join ',')'"; $failures.Add('project absent from the baseline') }

        $refused = $false
        try { Get-ProjectsWithoutBaseline $repository 'v9.9.9' @('Old/Old.csproj') | Out-Null }
        catch { $refused = "$_" -match 'v9\.9\.9 does not exist' }
        if ($refused) { Write-Host 'ok   a baseline tag that does not exist is refused' }
        else { Write-Host 'FAIL a baseline tag that does not exist is refused'; $failures.Add('missing baseline tag') }
        Invoke-Git @('checkout', '--quiet', 'main')

        $stamped = Get-ValidationVersion '0.16.0'
        if ($stamped -eq '0.16.1-validation') { Write-Host "ok   validation version above 0.16.0 -> $stamped" }
        else { Write-Host "FAIL validation version above 0.16.0 -> $stamped"; $failures.Add('validation version') }

        # The gate itself, on a repository with no release tag: it must fail before packing anything.
        $untagged = Join-Path $root 'untagged'
        New-Item -ItemType Directory -Path $untagged | Out-Null
        $repository = $untagged
        Invoke-Git @('init', '--quiet')
        Invoke-Git @('config', 'user.name', 'self-test')
        Invoke-Git @('config', 'user.email', 'self-test@example.invalid')
        Invoke-Git @('config', 'commit.gpgsign', 'false')
        New-Commit $null
        $gate = & pwsh -NoProfile -File $PSCommandPath -RepoRoot $untagged 2>&1
        if ($LASTEXITCODE -eq 1 -and "$gate" -match 'no vMAJOR\.MINOR\.PATCH tag is reachable') { Write-Host 'ok   no baseline fails the gate' }
        else { Write-Host "FAIL no baseline fails the gate -> exit $LASTEXITCODE`: $gate"; $failures.Add('no baseline') }

        $gate = & pwsh -NoProfile -File $PSCommandPath -RepoRoot $untagged -Baseline 'v0.16' 2>&1
        if ($LASTEXITCODE -eq 1 -and "$gate" -match "baseline 'v0\.16' is not MAJOR\.MINOR\.PATCH") { Write-Host 'ok   malformed baseline fails the gate' }
        else { Write-Host "FAIL malformed baseline fails the gate -> exit $LASTEXITCODE`: $gate"; $failures.Add('malformed baseline') }
    }
    finally
    {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    if ($failures.Count -gt 0)
    {
        Write-Host "Self-test FAILED: $($failures -join '; ')"
        return 1
    }

    Write-Host 'Self-test passed.'
    return 0
}

$exitCode = if ($SelfTest) { Invoke-SelfTest } else { Invoke-Validate }
exit $exitCode
