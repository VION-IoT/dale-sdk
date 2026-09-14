#requires -Version 7
<#
.SYNOPSIS
  Run every gate a PR is gated on and print one pass/fail line per gate. Optionally build
  and test, and optionally run in the shape the Linux CI runner uses.
.DESCRIPTION
  The gate list is DERIVED at runtime from .github/workflows/spec-gates.yml — the workflow is
  the authority on which gates run and in what order, and a gate added there without a line
  here fails this script instead of quietly not running. What each gate fails on is
  docs/spec-process.md § Gates; this script does not restate it.

  Each gate runs as its own pwsh process, with the arguments CI gives it. Two differences are
  deliberate: -RepoRoot is always passed (CI relies on the cwd), and a gate whose arguments
  need something the desk may not have runs without them, reported PARTIAL with what did not
  run. A gate that cannot run at all is reported SKIP with the reason rather than passing
  vacuously. A step using a VION-IoT/shared-workflows action is a gate too. Its script lives in
  that repository, so it runs from a local checkout of it — beside this repository unless
  -SharedWorkflowsRoot names another. With no checkout the gate FAILS rather than skipping: a
  skipped gate reads as a green check, and a journal nothing checked has failed the PR run twice.
  It is reported PARTIAL when the checkout is not at the commit the workflow's ref names on the
  checkout's origin, or when that origin cannot be asked, because its rules may differ from CI's.

  Not covered here: the ReSharper style gate (`scripts/cleanup-code.ps1 -Changed`, or the
  /cleanup command) and the packed-artifact gate, whose input is the publish job's .nupkg
  output rather than the working tree.

  -CiShape covers the three ways phase 1 of the SDD closeout found a check green at the desk
  and red on the Linux runner (docs/retro/journal-2026-08-12-to-2026-09-10.md, 2026-09-03 and
  2026-09-07). Only the
  first is a reproduction; the second is a scan, and it says so:

    1. The hidden-directory walk. On Linux a dot-directory is hidden, so `Get-ChildItem
       -Recurse` without -Force skips everything under .github/ and .claude/; on Windows it
       does not. This switch sets the Hidden attribute on the repository's top-level
       dot-directories for the duration of the run and restores it afterwards, which is the
       reproduction the T-005 fixture settled on. A gate whose walk omits -Force therefore
       scans FEWER files under -CiShape, and its own OK line's tally is where that shows —
       a count is what caught the original defect, not an exit code. The signal is a
       DIFFERENCE, so it needs a plain run to compare against; nothing here prints a baseline.
    2. Case-sensitive paths. Windows cannot be made case-sensitive for a run, so this one is
       a scan instead: every path-shaped literal in scripts/*.ps1 that names something git
       tracks must match the index's casing exactly. `Test-Path 'Vion.Dale.SDK'` is true on
       Windows and false on the runner. It reads scripts/*.ps1 and nothing else — both sites
       that bit were there — and it cannot see a path a script composes rather than quotes.
    3. `-p:Version=0.0.0-ci.1`. CI passes Version as a global MSBuild property, which beats a
       project's own <Version>; a fixture pinning a build literal reads 0.0.0-ci.N there and
       its own value here. Applied to -Build and -Test.
.EXAMPLE
  pwsh -File scripts/check.ps1
.EXAMPLE
  pwsh -File scripts/check.ps1 -CiShape -Test
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    # Build the solution after the gates. Off by default: spec-gates.yml is file-greps only
    # and runs on every PR precisely because it is cheap, and a check that always built would
    # not be run.
    [switch]$Build,
    # Test the solution after the gates.
    [switch]$Test,
    # Run in the Linux runner's shape — see .DESCRIPTION.
    [switch]$CiShape,
    # A checkout of VION-IoT/shared-workflows, whose actions' scripts the shared gates run.
    [string]$SharedWorkflowsRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if (-not $SharedWorkflowsRoot) { $SharedWorkflowsRoot = Join-Path (Split-Path -Parent $RepoRoot) 'shared-workflows' }

# The base ref CI diffs against: spec-gates.yml passes origin/$GITHUB_BASE_REF, and every PR
# in this repo targets main.
$baseRef = 'origin/main'
# The version literal CI packs and tests with (0.0.0-ci.{run_number}).
$ciVersion = '0.0.0-ci.1'

$workflow = Join-Path $RepoRoot '.github/workflows/spec-gates.yml'
if (-not (Test-Path -LiteralPath $workflow)) {
    Write-Host "check: FAIL - no .github/workflows/spec-gates.yml under '$RepoRoot' - nothing to derive the gate list from"
    exit 2
}

# How each derived gate is invoked at the desk. A gate the workflow runs with no entry here
# is a failure, not a skip: the workflow moved and this table did not.
#
# RefArgs are the arguments that need NeedsRef to resolve. They are dropped, not the gate:
# only spec-lint's narrative rule reads -Diff, and its other eight rules - malformed ACs,
# escape-hatch words, change-doc frontmatter and lifecycle - run without it. Skipping the
# whole gate for want of a fetch would hide those on every unfetched clone.
$invocation = @{
    'run-script-tests'    = @{ Args = @(); RepoRootArg = $false }
    'spec-lint'           = @{ Args = @(); RefArgs = @('-Diff', $baseRef); RepoRootArg = $true; NeedsRef = $baseRef
                               WithoutRef = 'the narrative rule needs a base ref; its other rules ran' }
    'spec-trace'          = @{ Args = @(); RepoRootArg = $true }
    'test-style-lint'     = @{ Args = @(); RepoRootArg = $true }
    'doc-comment-lint'    = @{ Args = @(); RepoRootArg = $true }
    'pragma-reason-lint'  = @{ Args = @(); RepoRootArg = $true }
    'bom-lint'            = @{ Args = @(); RepoRootArg = $true }
    'packed-msbuild-lint' = @{ Args = @(); RepoRootArg = $true }
    'sweep-residue-lint'  = @{ Args = @(); RepoRootArg = $true }
    'self-reference-lint' = @{ Args = @(); RepoRootArg = $true }
    # The workflow passes the action no inputs, so its defaults apply; the script's own parameter
    # defaults are the same values, and only the path needs anchoring to the repository.
    'journal-lint'        = @{ Shared = 'actions/journal-lint/journal-lint.ps1'; Args = @('-Path', (Join-Path $RepoRoot 'docs/process-journal.md')) }
}

# Derive the gate list, in the workflow's own order, from the scripts its steps invoke and the
# shared-workflows actions its steps use. The spec-lint step names its script twice (an if/else
# on GITHUB_BASE_REF), hence the dedupe. Comment lines are skipped: the workflow's header names
# every gate in prose, and a comment mentioning one the workflow does not run would mint a gate
# that is not one. Any other action - actions/checkout - is plumbing, not a gate.
$derived = [System.Collections.Generic.List[string]]::new()
$source = @{}
$pinnedRef = @{}
foreach ($line in (Get-Content -LiteralPath $workflow)) {
    if ($line -match '^\s*#') { continue }
    foreach ($m in [regex]::Matches($line, '\./scripts/([A-Za-z0-9._-]+)\.ps1|uses:\s*VION-IoT/shared-workflows/actions/([A-Za-z0-9._-]+)@([A-Za-z0-9._/-]+)')) {
        $isScript = $m.Groups[1].Success
        $name = if ($isScript) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        if (-not $derived.Contains($name)) {
            $derived.Add($name)
            $source[$name] = if ($isScript) { "scripts/$name.ps1" } else { "the shared-workflows action $name" }
            if (-not $isScript) { $pinnedRef[$name] = $m.Groups[3].Value }
        }
    }
}
if ($derived.Count -eq 0) {
    Write-Host 'check: FAIL - spec-gates.yml names no ./scripts/*.ps1 step and no shared-workflows action - the derivation is broken, not the repository'
    exit 2
}

$results = [System.Collections.Generic.List[psobject]]::new()
function Add-Result([string]$name, [string]$state, [string]$elapsed, [string]$detail, [string]$output, [string]$caveat) {
    $results.Add([pscustomobject]@{ Name = $name; State = $state; Elapsed = $elapsed; Detail = $detail; Output = $output; Caveat = $caveat })
}

# The last non-empty line of a gate's output is its own summary line - the line the passes
# used to paste by hand. Trimmed to one terminal line: spec-trace's OK line ends with all 67
# GAP ids, which would bury the eight lines around it.
function Get-Summary([string]$text) {
    $lines = @($text -split "`r?`n" | Where-Object { $_.Trim() })
    if (-not $lines.Count) { return '(no output)' }
    $summary = $lines[-1].Trim()
    if ($summary.Length -gt 140) { $summary = $summary.Substring(0, 137) + '...' }
    return $summary
}

function Invoke-Step([string]$name, [string]$exe, [string[]]$stepArgs, [string]$caveat) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = & $exe @stepArgs 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $sw.Stop()
    $state = if ($rc -eq 0) { 'PASS' } else { 'FAIL' }
    Add-Result $name $state ('{0:0.0}s' -f $sw.Elapsed.TotalSeconds) (Get-Summary $out) $out $caveat
}

$hiddenByUs = @()
try {
    # --- -CiShape, shape 1: make the dot-directories invisible the way Linux does ----------
    # Inside the try, so a directory that refuses the attribute does not leave its
    # predecessors hidden in the operator's working tree.
    if ($CiShape -and $IsWindows) {
        foreach ($d in (Get-ChildItem -LiteralPath $RepoRoot -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name.StartsWith('.') })) {
            if (-not ($d.Attributes -band [System.IO.FileAttributes]::Hidden)) {
                $d.Attributes = $d.Attributes -bor [System.IO.FileAttributes]::Hidden
                $hiddenByUs += $d
            }
        }
    }

    Write-Host ''
    Write-Host "check: $($derived.Count) gate(s), derived from .github/workflows/spec-gates.yml"
    if ($CiShape) {
        $shape1 =
            if (-not $IsWindows) { 'dot-directories are already hidden on this platform' }
            elseif ($hiddenByUs.Count) { "dot-directories hidden for this run: $(($hiddenByUs.Name | Sort-Object) -join ', ')" }
            else { 'every dot-directory was hidden already' }
        Write-Host "check: -CiShape - $shape1; -Build/-Test carry -p:Version=$ciVersion"
    }
    Write-Host ''

    foreach ($name in $derived) {
        $script = Join-Path $RepoRoot "scripts/$name.ps1"
        if (-not $invocation.ContainsKey($name)) {
            Add-Result $name 'FAIL' '-' "spec-gates.yml runs $($source[$name]) and check.ps1 knows no local invocation for it - add one to the `$invocation table" '' ''
            continue
        }
        if ($invocation[$name].Shared) {
            $sharedScript = Join-Path $SharedWorkflowsRoot $invocation[$name].Shared
            if (-not (Test-Path -LiteralPath $sharedScript)) {
                Add-Result $name 'FAIL' '-' "no VION-IoT/shared-workflows checkout at '$SharedWorkflowsRoot' - clone it there, or pass -SharedWorkflowsRoot, to run it" '' ''
                continue
            }
            # A checkout at another commit runs another version of the rules, so a pass there says
            # nothing certain about CI's verdict. It still runs: an older checkout catches most of
            # what CI would, and a skip would catch nothing. The ref is resolved on the origin, not
            # locally: the workflow pins a major tag that each release moves, and a fetch does not
            # overwrite a tag the checkout already has, so a local lookup finds the old commit.
            $ref = $pinnedRef[$name]
            $head = git -C $SharedWorkflowsRoot rev-parse --verify --quiet 'HEAD^{commit}' 2>$null
            $remote = @(git -C $SharedWorkflowsRoot ls-remote origin "refs/tags/$ref" "refs/tags/$ref^{}" "refs/heads/$ref" 2>$null)
            $reached = $LASTEXITCODE -eq 0
            # An annotated tag lists its own object and the commit it peels to; the peeled line wins.
            $lines = @($remote | Where-Object { $_ })
            $peeled = $lines | Where-Object { $_ -match '\^\{\}$' } | Select-Object -First 1
            $pinned = if ($peeled) { ($peeled -split '\s+')[0] } elseif ($lines.Count) { ($lines[0] -split '\s+')[0] } else { $null }
            $caveat = if (-not $reached) { "the checkout's origin could not be asked what '$ref' names, so its rules may differ from CI's" }
                      elseif (-not $pinned) { "the checkout's origin has no ref '$ref', which the workflow pins" }
                      elseif ($head -ne $pinned) { "the checkout is not at '$ref' ($($pinned.Substring(0, 8))), which the workflow pins, so its rules may differ from CI's; run 'git -C $SharedWorkflowsRoot fetch origin' and check out $($pinned.Substring(0, 8))" }
                      else { '' }
            Invoke-Step $name 'pwsh' (@('-NoProfile', '-File', $sharedScript) + $invocation[$name].Args) $caveat
            continue
        }
        if (-not (Test-Path -LiteralPath $script)) {
            Add-Result $name 'FAIL' '-' "spec-gates.yml runs scripts/$name.ps1 and no such script exists" '' ''
            continue
        }
        $spec = $invocation[$name]
        $stepArgs = @('-NoProfile', '-File', $script) + $spec.Args
        $caveat = ''
        if ($spec.NeedsRef) {
            git -C $RepoRoot rev-parse --verify --quiet "$($spec.NeedsRef)^{commit}" *> $null
            if ($LASTEXITCODE -eq 0) { $stepArgs += $spec.RefArgs }
            else {
                # The gate still runs; the rules that read the ref do not. spec-lint swallows a
                # -Diff ref git cannot resolve and reports OK having compared nothing, so passing
                # the ref anyway would turn one rule into a vacuous pass with nothing said.
                $caveat = "$($spec.NeedsRef) does not resolve, so $($spec.WithoutRef); run 'git fetch origin'"
            }
        }
        if ($spec.RepoRootArg) { $stepArgs += @('-RepoRoot', $RepoRoot) }
        Invoke-Step $name 'pwsh' $stepArgs $caveat
    }

    # --- -CiShape, shape 2: the path-case scan ---------------------------------------------
    if ($CiShape) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $canonical = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $folded = @{}
        # Directory paths, kept apart so a bare word can be compared against a directory name
        # without being compared against a file's. `docs`, `scripts`, `templates` are all
        # single-segment and dotless, and Join-Path takes them as literals throughout scripts/.
        $foldedDirs = @{}
        foreach ($p in (git -C $RepoRoot ls-files)) {
            $parts = $p -split '/'
            for ($i = 1; $i -le $parts.Count; $i++) {
                $sub = ($parts[0..($i - 1)] -join '/')
                if (-not $canonical.Add($sub)) { continue }
                $folded[$sub.ToLowerInvariant()] = $sub
                if ($i -lt $parts.Count) { $foldedDirs[$sub.ToLowerInvariant()] = $sub }
            }
        }
        # A path-shaped quoted literal: segments of name characters. One carrying a separator
        # or a dot is compared against every tracked path; a bare word only against tracked
        # directory names, because an ordinary string is far likelier to collide with a file's
        # stem than with a directory a script actually walks.
        $literalRx = "['`"]([A-Za-z0-9_.][A-Za-z0-9_.\-]*(?:/[A-Za-z0-9_.\-]+)*)['`"]"
        $checked = 0
        $mismatches = [System.Collections.Generic.List[string]]::new()
        foreach ($f in (Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'scripts') -Filter '*.ps1' -File -ErrorAction SilentlyContinue)) {
            $n = 0
            $inBlockComment = $false
            foreach ($line in (Get-Content -LiteralPath $f.FullName)) {
                $n++
                # Comment text is prose, not a call: a help block naming the wrong casing on
                # purpose is an example, not a defect. Only a whole-line or block comment is
                # stripped - a `#` trailing live code is left alone rather than parsed for.
                if ($inBlockComment) {
                    if ($line -match '#>') { $inBlockComment = $false }
                    continue
                }
                if ($line -match '<#') { if ($line -notmatch '#>') { $inBlockComment = $true }; continue }
                if ($line -match '^\s*#') { continue }
                foreach ($m in [regex]::Matches($line, $literalRx)) {
                    $lit = $m.Groups[1].Value
                    $key = $lit.ToLowerInvariant()
                    $index = if ($lit -match '[/.]') { $folded } else { $foldedDirs }
                    if (-not $index.ContainsKey($key)) { continue }
                    $checked++
                    if (-not $canonical.Contains($lit)) {
                        $mismatches.Add("scripts/$($f.Name):$n  '$lit' is tracked as '$($index[$key])' - true on Windows, false on the runner")
                    }
                }
            }
        }
        $sw.Stop()
        $elapsed = '{0:0.0}s' -f $sw.Elapsed.TotalSeconds
        if ($mismatches.Count) {
            Add-Result 'path-case' 'FAIL' $elapsed "$($mismatches.Count) literal(s) disagree with the index's casing" ($mismatches -join "`n")
        }
        elseif ($checked -eq 0) {
            # The anti-vacuous floor bom-lint and pragma-reason-lint carry: a scan that matched
            # nothing proves nothing, and a silently narrowed regex is how it gets there.
            Add-Result 'path-case' 'FAIL' $elapsed 'no path literal in scripts/*.ps1 matched a tracked path - the scan is looking at nothing' ''
        }
        else {
            Add-Result 'path-case' 'PASS' $elapsed "$checked path literal(s) in scripts/*.ps1 match the index's casing" ''
        }
    }

    # --- build and test, on request --------------------------------------------------------
    $solution = Join-Path $RepoRoot 'Vion.Dale.Sdk.sln'
    $versionArg = if ($CiShape) { @("-p:Version=$ciVersion") } else { @() }
    if ($Build) { Invoke-Step 'build' 'dotnet' (@('build', $solution) + $versionArg) }
    else { Add-Result 'build' 'SKIP' '-' 'not requested (pass -Build)' '' }
    if ($Test) { Invoke-Step 'test' 'dotnet' (@('test', $solution) + $versionArg) }
    else { Add-Result 'test' 'SKIP' '-' 'not requested (pass -Test)' '' }
}
finally {
    foreach ($d in $hiddenByUs) { $d.Attributes = $d.Attributes -band -bnot [System.IO.FileAttributes]::Hidden }
}

$width = ($results.Name | Measure-Object -Property Length -Maximum).Maximum
foreach ($r in $results) {
    Write-Host ("  {0,-4}  {1}  {2,6}  {3}" -f $r.State, $r.Name.PadRight($width), $r.Elapsed, $r.Detail)
    # A gate that ran with one of its rules disabled is not a plain pass, and a caveat on its
    # own line under the gate is the only place a reader would look for that.
    if ($r.Caveat) { Write-Host ("  {0}    {1}  PARTIAL: {2}" -f (' ' * 4), (' ' * $width), $r.Caveat) }
}

$failed = @($results | Where-Object { $_.State -eq 'FAIL' })
foreach ($r in $failed) {
    if ($r.Output) {
        Write-Host ''
        Write-Host "--- $($r.Name) ---"
        $r.Output -split "`r?`n" | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "    $_" }
    }
}

$skipped = @($results | Where-Object { $_.State -eq 'SKIP' })
Write-Host ''
if ($failed.Count) {
    Write-Host "check: FAIL - $($failed.Count) of $($results.Count) step(s) failed: $(($failed.Name) -join ', ')"
    exit 1
}
$partial = @($results | Where-Object { $_.Caveat })
$tail = if ($skipped.Count) { ", $($skipped.Count) skipped" } else { '' }
if ($partial.Count) { $tail += "; $($partial.Count) ran partially: $(($partial.Name) -join ', ')" }
Write-Host "check: OK - $($results.Count - $skipped.Count) step(s) passed$tail"
exit 0
