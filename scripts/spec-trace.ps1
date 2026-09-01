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
  an orphan.
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

# Fold in active change-doc deltas (docs/changes/*.md top level, status: in-flight).
# ADDED/MODIFIED ids must be test-referenced (they may not be in a page yet);
# REMOVED ids are exempt (being deleted). Delta-line grammar only, so ids in prose
# don't create false declarations.
$changesDir = Join-Path $RepoRoot 'docs/changes'
$deltaIdRx = '(?m)^\s*-?\s*(ADDED|MODIFIED|REMOVED)\s+`?((?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?)`?\s*->'
if (Test-Path $changesDir) {
    foreach ($cd in (Get-ChildItem -LiteralPath $changesDir -Filter *.md -File)) {   # top-level only; archive/ excluded
        if ($cd.Name -like '_*') { continue }
        $craw = Get-Content -LiteralPath $cd.FullName -Raw
        if ($craw -notmatch '(?m)^status:\s*in-flight') { continue }
        foreach ($m in [regex]::Matches($craw, $deltaIdRx)) {
            if ($m.Groups[1].Value -eq 'REMOVED') { [void]$declared.Remove($m.Groups[2].Value) }
            else { [void]$declared.Add($m.Groups[2].Value) }
        }
    }
}

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
