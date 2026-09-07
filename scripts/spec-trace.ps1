#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if any AC-/SYS- id declared by a traced spec page (frontmatter
  `trace: enforced`) or by an in-flight change doc's Spec delta has no
  referencing test. Ported from logic-block-libraries' spec-trace.ps1, adapted:
  flat corpus under docs/specs/, per-page opt-in ratchet instead of per-library
  resolution.

  An id counts as COVERED only when it appears as a QUOTED STRING LITERAL —
  MSTest [TestProperty("spec", "AC-…")], xunit [Trait("spec", "AC-…")], or a
  scenario file's "specs": ["AC-…"] — in the scanned roots: every *.Test
  directory anywhere in the repo (examples/, libraries/ and templates/ carry
  the xunit projects) plus Vion.Dale.DevHost.SmokeHost. A bare mention in a
  comment or method name does not count. A bare umbrella id (no ".M") is
  additionally covered by any of its leaves. An id declared on a line carrying
  the GAP marker is exempt-but-counted: reported as awaiting its test, never
  an orphan. A leaf missing below its umbrella's highest leaf (`AC-X-001.2` absent while
  `.1` and `.3` exist) is a hole: fine when a change doc — in-flight or archived — carries a
  `REMOVED <id> ->` delta line for it, the record of a criterion withdrawn or merged; a FAIL
  otherwise. An unexplained hole once hid a classified row with no criterion behind it. Any
  archive mention of the id used to satisfy this, which made the check nearly vacuous: the pass
  that minted an id writes its `ADDED` line into the same archive, so every hole explained its
  own absence.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    # List the files behind the warn-only cross-tier-clause note (rounds work the backlog).
    [switch]$CrossTierDetail
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'spec-trace: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$specsDir = Join-Path $RepoRoot 'docs/specs'
$idRx = '\b(?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?\b'
# The delta-line grammar of docs/spec-process.md § Change docs: `OP <id> -> <target> : <payload>`.
# Both readers below parse it — the scan for the REMOVED lines that explain id-sequence holes, and
# the in-flight scan that folds a change doc's own delta into the declared set. The
# captured id runs to the arrow, so `AC-X-001.2` is not read out of `AC-X-001.20`.
$deltaIdRx = '^\s*-?\s*(ADDED|MODIFIED|REMOVED)\s+`?((?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?)`?\s*->'

$declared = [System.Collections.Generic.HashSet[string]]::new()
# Ids declared on a line carrying the GAP marker: known-untested backlog rows
# (docs/spec-process.md) — exempt from the orphan check, surfaced as a count.
$gapIds = [System.Collections.Generic.HashSet[string]]::new()
$tracedPages = 0
if (Test-Path $specsDir) {
    foreach ($f in (Get-ChildItem -LiteralPath $specsDir -Recurse -Filter *.md)) {
        $raw = Get-Content -Raw -LiteralPath $f.FullName
        if ($raw -notmatch '(?m)^trace:\s*enforced\s*$') { continue }
        $tracedPages++
        $idsInPage = 0
        foreach ($line in @(Get-Content -LiteralPath $f.FullName)) {
            foreach ($m in [regex]::Matches($line, $idRx)) {
                $idsInPage++
                if ($line -cmatch '\bGAP\b') { [void]$gapIds.Add($m.Value) }
                else { [void]$declared.Add($m.Value) }
            }
        }
        if ($idsInPage -eq 0) {
            # Anti-vacuous floor: a page that OPTED INTO tracing but yields zero ids
            # means the parse died (a format change) — and a gate that can silently
            # match nothing can silently die with nothing else in CI noticing.
            $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
            Write-Host "spec-trace: FAIL - $rel is 'trace: enforced' but ZERO AC-/SYS- ids parsed (the parse died - anti-vacuous floor)."
            exit 1
        }
    }
}
# An id both declared normally and marked GAP somewhere is declared — the GAP
# exemption covers only ids that appear on GAP lines exclusively.
$gapIds.ExceptWith($declared)

# Id-sequence holes on the traced pages. Every hole must carry a REMOVED delta line in a change
# doc — that is how a doc records a criterion it withdrew or merged. Prose about the id is not that
# record: it satisfied this check until 2026-09-07, and so did the ADDED line of the pass that
# minted the id, which every withdrawn id has. A hole nobody recorded is the shape that hid a
# classified row with no criterion behind it.
#
# Archived docs AND started ones, because the doc doing the withdrawing is in-flight for exactly as
# long as its own PRs run: reading the archive alone reddens a leaf retirement from the edit that
# opens the hole until the archive commit that closes the change. Not a `proposed` doc, which is
# reviewed-but-not-started (§ Change docs) and so cannot have opened the hole its line closes, and
# not a line inside a fence, which is the grammar quoted as an example rather than a record.
$changesDir = Join-Path $RepoRoot 'docs/changes'
$removedInChangeDocs = [System.Collections.Generic.HashSet[string]]::new()
$deltaDocs = @()
if (Test-Path $changesDir) { $deltaDocs += @(Get-ChildItem -LiteralPath $changesDir -Filter *.md -File) }
$archiveDir = Join-Path $changesDir 'archive'
if (Test-Path $archiveDir) { $deltaDocs += @(Get-ChildItem -LiteralPath $archiveDir -Filter *.md -File) }
$deltaDocs = @($deltaDocs | Where-Object { $_.Name -notlike '_*' })   # scaffolding, never a change doc
foreach ($f in $deltaDocs) {
    $lines = @(Get-Content -LiteralPath $f.FullName)
    if (($lines -join "`n") -match '(?m)^status:\s*proposed') { continue }
    $inFence = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $m = [regex]::Match($line, $deltaIdRx)
        if ($m.Success -and $m.Groups[1].Value -eq 'REMOVED') { [void]$removedInChangeDocs.Add($m.Groups[2].Value) }
    }
}
$leavesByUmbrella = @{}
foreach ($id in @($declared) + @($gapIds)) {
    if ($id -match '^((?:AC|SYS)-[A-Z0-9]+-\d+)\.(\d+)$') {
        $u = $Matches[1]
        if (-not $leavesByUmbrella.ContainsKey($u)) { $leavesByUmbrella[$u] = [System.Collections.Generic.HashSet[int]]::new() }
        [void]$leavesByUmbrella[$u].Add([int]$Matches[2])
    }
}
$holes = [System.Collections.Generic.List[string]]::new()
foreach ($u in $leavesByUmbrella.Keys) {
    $max = ($leavesByUmbrella[$u] | Measure-Object -Maximum).Maximum
    for ($k = 1; $k -le $max; $k++) {
        if ($leavesByUmbrella[$u].Contains($k)) { continue }
        $hole = "$u.$k"
        if (-not $removedInChangeDocs.Contains($hole)) { $holes.Add($hole) }
    }
}
if ($holes.Count) {
    Write-Host "spec-trace: FAIL - $($holes.Count) id-sequence hole(s) with no ``REMOVED <id> -> <page> : <reason>`` line in any change doc (that line is how a withdrawn or merged criterion is recorded; an unexplained hole hid a classified row with no criterion):"
    $holes | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# Fold in active change-doc deltas (docs/changes/*.md top level, status: in-flight).
# ADDED/MODIFIED ids must be test-referenced (they may not be in a page yet);
# REMOVED ids are exempt (being deleted). Delta-line grammar only, so ids in prose
# don't create false declarations.
if (Test-Path $changesDir) {
    foreach ($cd in (Get-ChildItem -LiteralPath $changesDir -Filter *.md -File)) {   # top-level only; archive/ excluded
        if ($cd.Name -like '_*') { continue }
        $craw = Get-Content -LiteralPath $cd.FullName -Raw
        if ($craw -notmatch '(?m)^status:\s*in-flight') { continue }
        # Line-wise, so a delta line carrying the GAP marker is exempt-but-counted exactly like a
        # page line is — otherwise a legitimately GAP'd criterion reads as a hard FAIL from the
        # moment its delta is written until the doc archives.
        foreach ($line in @(Get-Content -LiteralPath $cd.FullName)) {
            $m = [regex]::Match($line, $deltaIdRx)
            if (-not $m.Success) { continue }
            $id = $m.Groups[2].Value
            if ($m.Groups[1].Value -eq 'REMOVED') { [void]$declared.Remove($id) }
            elseif ($line -cmatch '\bGAP\b') { [void]$gapIds.Add($id) }
            else { [void]$declared.Add($id) }
        }
    }
}
$gapIds.ExceptWith($declared)

if ($declared.Count -eq 0) {
    $gapTail = if ($gapIds.Count) { "; $($gapIds.Count) GAP id(s) awaiting tests" } else { '' }
    Write-Host "spec-trace: no traced ids yet ($tracedPages traced page(s), no in-flight deltas$gapTail) - skipping"
    exit 0
}

# Scan roots: every *.Test directory anywhere in the repo — the xunit projects live
# nested (examples/<name>/<name>.Test, libraries/…, templates/…) — plus the SmokeHost
# (committed scenarios).
$testRoots = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Directory -Filter '*.Test' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git|node_modules)[\\/]' } | ForEach-Object { $_.FullName })
$smokeHost = Join-Path $RepoRoot 'Vion.Dale.DevHost.SmokeHost'
if (Test-Path $smokeHost) { $testRoots += $smokeHost }

$referenced = [System.Collections.Generic.HashSet[string]]::new()
$quotedIdRx = '"((?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?)"'
# For the cross-tier warn below: which .cs files cite an id, and whether each file
# already carries a "Cross-tier" clause (an id proven by BOTH a unit test and a
# scenario states which half each tier owns in the class summary).
$traitFiles = @{}
$fileHasCrossTier = @{}
Get-ChildItem -Path $testRoots -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $raw = Get-Content -LiteralPath $_.FullName -Raw
        $file = $_.FullName
        $fileHasCrossTier[$file] = $raw -match '(?i)cross-tier'
        [regex]::Matches($raw, $quotedIdRx) |
            ForEach-Object {
                $id = $_.Groups[1].Value
                [void]$referenced.Add($id)
                if (-not $traitFiles.ContainsKey($id)) { $traitFiles[$id] = [System.Collections.Generic.HashSet[string]]::new() }
                [void]$traitFiles[$id].Add($file)
            }
    }

# Committed scenario files: AC ids in a scenario's quoted `specs`/`spec` fields count
# toward coverage — this makes a judgment criterion a scenario demonstrates first-class
# traceable, the class the unit tiers cannot honestly cover.
$scenarioIds = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem -Path $testRoots -Recurse -Filter *.scenario.json -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw), $quotedIdRx) |
            ForEach-Object { [void]$referenced.Add($_.Groups[1].Value); [void]$scenarioIds.Add($_.Groups[1].Value) }
    }

# WARN-ONLY (never fails): an id proven BOTH by a unit citation and a scenario claim
# needs a "Cross-tier" clause in (at least one of) its citing files' class summaries.
$missingClause = @{}
foreach ($id in $traitFiles.Keys) {
    if (-not $scenarioIds.Contains($id)) { continue }
    if (@($traitFiles[$id] | Where-Object { $fileHasCrossTier[$_] }).Count -gt 0) { continue }
    foreach ($f in $traitFiles[$id]) {
        if (-not $missingClause.ContainsKey($f)) { $missingClause[$f] = [System.Collections.Generic.List[string]]::new() }
        $missingClause[$f].Add($id)
    }
}
if ($missingClause.Count) {
    if ($CrossTierDetail) {
        Write-Host "spec-trace: WARN - $($missingClause.Count) test file(s) cite ids a scenario also claims, with no 'Cross-tier' clause in the file:"
        foreach ($f in ($missingClause.Keys | Sort-Object)) {
            $rel = [System.IO.Path]::GetRelativePath($RepoRoot, $f).Replace('\', '/')
            Write-Host "  $rel  ($(($missingClause[$f] | Sort-Object | Select-Object -First 6) -join ', ')$(if ($missingClause[$f].Count -gt 6) { ', …' }))"
        }
    } else {
        Write-Host "spec-trace: note - $($missingClause.Count) test file(s) cite scenario-claimed ids without a 'Cross-tier' clause (list: -CrossTierDetail)"
    }
}

# An id is covered when a test references it directly. A bare PARENT id (no ".<n>"
# leaf suffix) is ADDITIONALLY covered when any of its leaf children is referenced:
# the parent is an organizational umbrella and the leaves carry the testable criteria.
# Leaf ids get NO such exemption, so genuine leaf gaps are still reported.
function Test-SpecCovered([string]$id) {
    if ($referenced.Contains($id)) { return $true }
    if ($id -notmatch '\.\d+$') {
        $childRx = '^' + [regex]::Escape($id) + '\.\d+$'
        foreach ($ref in $referenced) {
            if ($ref -match $childRx) { return $true }
        }
    }
    return $false
}
$orphans = @($declared | Where-Object { -not (Test-SpecCovered $_) } | Sort-Object)
if ($orphans.Count) {
    Write-Host "spec-trace: FAIL - $($orphans.Count) id(s) with no test reference:"
    $orphans | ForEach-Object { Write-Host "  $_" }
    exit 1
}
$gapTail = if ($gapIds.Count) { "; $($gapIds.Count) GAP id(s) awaiting tests: $(@($gapIds | Sort-Object) -join ', ')" } else { '' }
Write-Host "spec-trace: OK - $($declared.Count) id(s) all referenced by tests ($tracedPages traced page(s)$gapTail)"
exit 0
