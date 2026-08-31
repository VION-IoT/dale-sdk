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

  Untracked files count, both ways. `git diff` sees tracked files only, but cleanupcode
  rewrites a brand-new .cs just the same - so untracked .cs are added to -Changed's scope
  and hashed either side of the run for drift. Without that, a branch of brand-new files
  reported "clean" while every one of them had been reformatted.

  -Verify refuses to run on a tree with uncommitted tracked changes (exit 2). Its drift
  signal is `git diff HEAD`, which cannot tell cleanup's rewrites from the developer's own
  edits, so on a dirty tree it reported style drift that was never about style. Two sessions
  chased that phantom before committing and getting "Code style: clean" (process journal,
  2026-08-28 and 2026-08-31). CI checks out clean, so the gate path is unaffected.

.PARAMETER Verify
  CI mode: after cleanup, fail with a non-zero exit code if anything changed. Refuses to run
  (exit 2) when the tree has uncommitted tracked changes - see the description.

.PARAMETER NoBuild
  Skip `dotnet build` (use when the solution is already built, e.g. in CI where a
  prior step built it). cleanupcode needs up-to-date build output.

.PARAMETER Changed
  Dev-loop fast mode: scope cleanupcode to the .cs files this branch changed (vs
  origin/main), plus the working tree, plus untracked .cs, and skip entirely when no .cs
  changed. The 'Full Cleanup (excl. optimize usings)' profile is per-file (no cross-file
  edits), so a scoped pass is equivalent for the changed files. Run this once right before a
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

# Files under a path cleanupcode never touches, or that `dotnet build` rewrites on its own, are
# not a code-style signal. wwwroot is excluded from the run above; lock files are restore output.
$notCodeStyle = '(^|/)packages\.lock\.json$|^Vion\.Dale\.DevHost\.Web/wwwroot/'

# Untracked, non-ignored files. `git diff` cannot see these, but cleanupcode rewrites them in
# place all the same — they are both scoped into -Changed and hashed for drift below.
function Get-UntrackedFiles {
    @(git ls-files --others --exclude-standard | Where-Object { $_ -and ($_ -notmatch $notCodeStyle) })
}

function Show-Drift {
    param([string[]]$Tracked, [string[]]$Untracked)

    if ($Tracked.Count -gt 0) {
        # Measured against HEAD, so a local run also lists the developer's own uncommitted edits.
        # Under -Verify the checkout is clean, so there it is exactly what cleanup changed.
        Write-Host '  modified vs HEAD (a local run also lists edits you made yourself):'
        git diff HEAD --stat -- $Tracked
    }
    if ($Untracked.Count -gt 0) {
        # `git diff` cannot render these — they have no committed side to diff against.
        Write-Host '  untracked, rewritten in place (git add them before -Verify can judge the branch):'
        $Untracked | ForEach-Object { Write-Host "    $_" }
    }
}

Push-Location $repoRoot
try {
    # -Verify's drift signal is `git diff HEAD` after the run, which cannot separate cleanup's rewrites
    # from edits the developer already had in the tree - so on a dirty tree it reports "style drift" for
    # changes that have nothing to do with style. Refuse up front, naming the cause, instead of running a
    # full build + cleanup to produce a misleading verdict. Exit 2, distinct from the gate's own exit 1.
    # Untracked files are exempt: they are judged by content hash either side of the run, which is exact.
    if ($Verify) {
        $uncommitted = @(git diff HEAD --name-only | Where-Object { $_ -and ($_ -notmatch $notCodeStyle) })
        if ($uncommitted.Count -gt 0) {
            Write-Host "-Verify needs a clean tree: $($uncommitted.Count) tracked file(s) have uncommitted changes."
            Write-Host '  -Verify judges drift with `git diff HEAD`, so your own edits would be reported as style drift.'
            Write-Host '  Commit them first, then re-run -Verify. To clean the tree instead, run without -Verify:'
            Write-Host '    pwsh scripts/cleanup-code.ps1 -Changed'
            $uncommitted | ForEach-Object { Write-Host "    $_" }
            exit 2
        }
    }

    # -Changed scopes the (slow) full-solution cleanup to just the .cs this branch touched (vs origin/main +
    # the working tree). The profile is per-file, so the result is identical for those files — the dev-loop
    # fast path. Detect FIRST: when no .cs changed, skip restore+build+cleanup entirely. CI stays full (-Verify).
    $includeArgs = @()
    if ($Changed) {
        # The three `git diff` forms below see TRACKED files only. A brand-new .cs that has not been
        # `git add`ed is invisible to all of them, so it used to slip past the scoped pass entirely and
        # only surface as CI style drift. `git ls-files --others` is the fourth source that closes it.
        $csFiles = @(
            (git diff --name-only 'origin/main...HEAD' 2>$null)
            (git diff --name-only 2>$null)
            (git diff --name-only --cached 2>$null)
            (Get-UntrackedFiles)
        ) | Where-Object { $_ -and $_.EndsWith('.cs') } | Sort-Object -Unique
        if ($csFiles.Count -eq 0) {
            Write-Host 'No changed .cs files (vs origin/main + working tree + untracked) - skipping cleanupcode.'
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

    # Snapshot untracked files before the run: `git diff` has no committed side to diff them
    # against, so a content hash is the only way to notice cleanupcode rewrote one.
    $hashBefore = @{}
    foreach ($file in Get-UntrackedFiles) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $hashBefore[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        }
    }

    $output = & dotnet jb cleanupcode --no-build --verbosity=ERROR "--profile=$cleanupProfile" "--exclude=$excludePaths" @includeArgs $solution 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'cleanupcode failed to run:'
        $output
        exit 1
    }

    # Restore-generated NuGet lock files aren't a code-style signal: `dotnet build` can
    # rewrite them (environment-dependent, not enforced via locked-mode here) and cleanupcode
    # never touches them. Consider drift on everything except those. Filtering in PowerShell
    # avoids git pathspec/glob quoting quirks under pwsh.
    #
    # `git diff HEAD`, not bare `git diff`: the latter compares the working tree against the INDEX,
    # so drift in a file the developer had already `git add`ed would be invisible here. In CI the
    # index equals HEAD and the two are identical; locally — the only place this prints — they are not.
    $trackedDrift = @(git diff HEAD --name-only | Where-Object { $_ -and ($_ -notmatch $notCodeStyle) })
    $untrackedDrift = @($hashBefore.Keys | Where-Object {
            (Test-Path -LiteralPath $_ -PathType Leaf) -and
            (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash -ne $hashBefore[$_]
        } | Sort-Object)
    $isClean = ($trackedDrift.Count -eq 0 -and $untrackedDrift.Count -eq 0)

    if ($Verify) {
        if (-not $isClean) {
            Write-Host "::error::Code style drift. Run 'pwsh scripts/cleanup-code.ps1' (or /cleanup) and commit the result."
            Show-Drift $trackedDrift $untrackedDrift
            exit 1
        }
        Write-Host 'Code style: clean.'
    }
    else {
        if (-not $isClean) {
            Write-Host "cleanupcode applied changes - review with 'git diff' and commit:"
            Show-Drift $trackedDrift $untrackedDrift
        }
        else {
            Write-Host 'Already clean - nothing to do.'
        }
    }
}
finally {
    Pop-Location
}
