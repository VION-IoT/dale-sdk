#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if the spec corpus or the change-doc lane is malformed. Ported from
  logic-block-libraries' spec-lint.ps1, adapted: flat corpus (no tiers/Inherits),
  plus the change-doc lifecycle checks (docs/changes/).

  Corpus (docs/specs/*.md):
    - an AC declaration missing the EARS 'SHALL' predicate
    - an escape-hatch word (should/fast/performant) inside an AC block
    - a malformed declared id (shape: AC-<AREA>-NNN[.M] / SYS-<AREA>-NNN, uppercase)
    - with -Diff <ref>: warns on change-narrative markers ADDED to a current-truth
      page since <ref> (-Strict turns the warning into a failure)

  Change docs (docs/changes/):
    - unknown/missing status frontmatter (proposed|in-flight|parked|archived)
    - a doc marked archived outside archive/, or a doc inside archive/ not archived
    - parked without a blocked-on reason
    Underscore-prefixed files (_template.md) are not change docs and are skipped.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$Diff,      # base ref: enable the narrative rule over lines added since <Diff>
    [switch]$Strict     # narrative rule hard-fails instead of warning
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'spec-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$specsDir = Join-Path $RepoRoot 'docs/specs'
$changesDir = Join-Path $RepoRoot 'docs/changes'

# Identifies an AC *declaration* bullet: id followed by an EARS `(label):`. The
# `(label):` is what separates a declaration from a prose cross-reference.
$acStartRx   = '^\s*-\s+`?(AC-[A-Z0-9]+-\d+(?:\.\d+)?)`?\s*\([^)]+\):'
# Loose: captures the id-ish token at a declaration site so a MALFORMED id is
# still seen (and then fails $wellFormed).
$declLooseRx = '^\s*-\s+`?((?:AC|SYS)-[A-Za-z0-9.\-]+)`?\s*\([^)]+\):'
$wellFormed  = '^(?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?$'
# Escape-hatch words, but not inside a hyphenated/word compound (fast-charge is fine).
$escapeRx    = '(?i)(?<![-\w])(should|fast|performant)(?![-\w])'
# Change-narrative markers that must not be ADDED to a current-truth page.
$narrativeRx = '(?i)(History note|[A-Z0-9]+-DRIFT\b|Resolution of OPEN-|Drift checkpoint)'

$problems = [System.Collections.Generic.List[string]]::new()

if (Test-Path $specsDir) {
    foreach ($f in (Get-ChildItem -LiteralPath $specsDir -Recurse -Filter *.md)) {
        # @() so a single-line file stays an array — a bare Get-Content returns a string
        # there, and indexing a string yields characters, silently skipping every check.
        $lines = @(Get-Content -LiteralPath $f.FullName)
        $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]

            # AC block: bullet + contiguous continuation lines (until blank / new
            # top-level bullet / heading). Indented sub-bullets stay in the block.
            if ($line -match $acStartRx) {
                $acId = $Matches[1]
                $block = $line
                $j = $i + 1
                while ($j -lt $lines.Count -and $lines[$j].Trim() -ne '' -and
                       $lines[$j] -notmatch '^-\s' -and $lines[$j] -notmatch '^\s*#') {
                    $block += "`n" + $lines[$j]; $j++
                }
                # -cnotmatch: the EARS keyword is uppercase by definition; a lowercase
                # 'shall' is a prose escape, not a predicate.
                if ($block -cnotmatch '\bSHALL\b') { $problems.Add("${rel}: $acId - no EARS 'SHALL' predicate") }
                if ($block -match $escapeRx)      { $problems.Add("${rel}: $acId - escape-hatch '$($Matches[1])'") }
            }

            # Declared id well-formedness (declaration sites only). -cnotmatch:
            # case-SENSITIVE, so a lowercase area (AC-a-001.1) fails.
            if ($line -match $declLooseRx) {
                $id = $Matches[1]
                if ($id -cnotmatch $wellFormed) { $problems.Add("${rel}: malformed declared id '$id'") }
            }
        }
    }
}

# Change-doc lifecycle.
if (Test-Path $changesDir) {
    foreach ($f in (Get-ChildItem -LiteralPath $changesDir -Recurse -Filter *.md -File)) {
        if ($f.Name -like '_*') { continue }
        $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
        $raw = Get-Content -Raw -LiteralPath $f.FullName
        $inArchive = $f.DirectoryName -match '[\\/]archive$'
        $status = ([regex]::Match($raw, '(?m)^status:\s*(\S+)')).Groups[1].Value
        if ($status -notin @('proposed', 'in-flight', 'parked', 'archived')) {
            $problems.Add("${rel}: unknown or missing status '$status'")
            continue
        }
        if ($status -eq 'archived' -and -not $inArchive) { $problems.Add("${rel}: status archived but not under archive/") }
        if ($status -ne 'archived' -and $inArchive)      { $problems.Add("${rel}: under archive/ but status '$status'") }
        if ($status -eq 'parked') {
            $blocked = ([regex]::Match($raw, '(?m)^blocked-on:\s*(.+?)\s*$')).Groups[1].Value
            if (-not $blocked -or $blocked -eq 'none') { $problems.Add("${rel}: parked without a blocked-on reason") }
        }
    }
}

# Narrative rule (diff-scoped): change narrative ADDED to a current-truth page. Warn by
# default; -Strict turns it into a hard failure.
$narrativeHits = [System.Collections.Generic.List[string]]::new()
if ($Diff -and (Test-Path $specsDir)) {
    $added = git -C $RepoRoot diff --unified=0 --no-color "$Diff...HEAD" -- 'docs/specs' 2>$null
    foreach ($l in $added) {
        if ($l -match '^\+' -and $l -notmatch '^\+\+\+' -and $l -match $narrativeRx) {
            $narrativeHits.Add($l.TrimStart('+').Trim())
        }
    }
    foreach ($h in $narrativeHits) {
        Write-Host "::warning::spec-lint - change narrative added to a current-truth spec page; move it to the change doc: $h"
    }
}

$failed = ($problems.Count -gt 0) -or ($Strict -and $narrativeHits.Count -gt 0)
if ($problems.Count) {
    Write-Host "spec-lint: FAIL - $($problems.Count) issue(s):"
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
}
if ($failed) { exit 1 }
$warn = if ($narrativeHits.Count) { " ($($narrativeHits.Count) narrative warning(s))" } else { "" }
Write-Host "spec-lint: OK$warn"
exit 0
