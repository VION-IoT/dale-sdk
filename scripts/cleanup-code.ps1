#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Run the ReSharper code-cleanup that the CI style gate enforces - locally, before a PR.

.DESCRIPTION
  Single source of truth for the `dotnet jb cleanupcode` invocation: restores the
  pinned JetBrains tool (.config/dotnet-tools.json), builds the solution, and runs
  cleanup with the exact profile CI uses (the same profile ReSharper/Rider apply on
  save, defined in Vion.Dale.Sdk.sln.DotSettings). CI runs this script with -Verify, so
  the local cleanup and the CI gate can never diverge.

  Default (apply) mode rewrites files in place - review `git diff` and commit.
  -Verify mode additionally fails (exit 1) if cleanup changed anything (the CI gate).

  cleanupcode is noisy (Metalama + source generators); the gate signal is the exit
  code + git diff, not stdout, so this script captures cleanupcode's output and only
  shows it if cleanupcode itself fails.

.PARAMETER Verify
  CI mode: after cleanup, fail with a non-zero exit code if anything changed.

.PARAMETER NoBuild
  Skip `dotnet build` (use when the solution is already built, e.g. in CI where a
  prior step built it). cleanupcode needs up-to-date build output.

.PARAMETER Changed
  Dev-loop fast mode: scope cleanupcode to the .cs files this branch changed (vs
  origin/main) plus the working tree, and skip entirely when no .cs changed. The
  'Full Cleanup (excl. optimize usings)' profile is per-file (no cross-file edits),
  so a scoped pass is equivalent for the changed files. Run this once right before a
  PR; the full-solution default and the CI gate (-Verify) stay the authoritative backstop.

.EXAMPLE
  pwsh scripts/cleanup-code.ps1
  Clean the whole solution, then review `git diff` and commit the result.

.EXAMPLE
  pwsh scripts/cleanup-code.ps1 -Changed
  Clean only the .cs this branch touched (fast). Review `git diff` and commit.

.EXAMPLE
  pwsh scripts/cleanup-code.ps1 -Verify -NoBuild
  The CI gate: fail if the tree is not already clean.
#>
[CmdletBinding()]
param(
    [switch]$Verify,
    [switch]$NoBuild,
    [switch]$Changed
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = 'Vion.Dale.Sdk.sln'
$cleanupProfile = 'Custom: Full Cleanup (excl. optimize usings)'
# The DevHost web UI's static assets (hand-written JS/HTML + vendored minified libraries) are not
# C# style-gate material: the DotSettings profile would reformat them and fight both the vendored
# files and deliberate hand-formatting. Excluded here — the single source of truth for the gate —
# so local cleanup and CI agree.
$excludePaths = 'Vion.Dale.DevHost.Web/wwwroot/**'

Push-Location $repoRoot
try {
    # -Changed scopes the (slow) full-solution cleanup to just the .cs this branch touched (vs origin/main +
    # the working tree). The profile is per-file, so the result is identical for those files — the dev-loop
    # fast path. Detect FIRST: when no .cs changed, skip restore+build+cleanup entirely. CI stays full (-Verify).
    $includeArgs = @()
    if ($Changed) {
        $csFiles = @(
            (git diff --name-only 'origin/main...HEAD' 2>$null)
            (git diff --name-only 2>$null)
            (git diff --name-only --cached 2>$null)
        ) | Where-Object { $_ -and $_.EndsWith('.cs') } | Sort-Object -Unique
        if ($csFiles.Count -eq 0) {
            Write-Host 'No changed .cs files (vs origin/main + working tree) - skipping cleanupcode.'
            exit 0
        }

        # --include resolves against the .sln directory; the solution sits at the repo root, so git's
        # repo-relative paths are already solution-relative.
        $includeArgs = @("--include=$($csFiles -join ';')")
        Write-Host "Cleanup scoped to $($csFiles.Count) changed .cs file(s)."
    }

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { Write-Host 'dotnet tool restore failed.'; exit 1 }

    if (-not $NoBuild) {
        dotnet build $solution
        if ($LASTEXITCODE -ne 0) { Write-Host 'dotnet build failed.'; exit 1 }
    }

    $output = & dotnet jb cleanupcode --no-build --verbosity=ERROR "--profile=$cleanupProfile" "--exclude=$excludePaths" @includeArgs $solution 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'cleanupcode failed to run:'
        $output
        exit 1
    }

    # Restore-generated NuGet lock files aren't a code-style signal: `dotnet build` can
    # rewrite them (environment-dependent, not enforced via locked-mode here) and cleanupcode
    # never touches them. Consider drift on everything except lock files. (dale/dale-sdk have
    # none.) Filtering in PowerShell avoids git pathspec/glob quoting quirks under pwsh.
    $drift = @(git diff --name-only | Where-Object { $_ -and ($_ -notmatch '(^|/)packages\.lock\.json$') })
    $isClean = ($drift.Count -eq 0)

    if ($Verify) {
        if (-not $isClean) {
            Write-Host "::error::Code style drift. Run 'pwsh scripts/cleanup-code.ps1' (or /cleanup) and commit the result."
            git diff --stat -- $drift
            exit 1
        }
        Write-Host 'Code style: clean.'
    }
    else {
        if (-not $isClean) {
            Write-Host "cleanupcode applied changes - review with 'git diff' and commit:"
            git diff --stat -- $drift
        }
        else {
            Write-Host 'Already clean - nothing to do.'
        }
    }
}
finally {
    Pop-Location
}
