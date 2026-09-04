#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if docs/process-journal.md breaks its own format: every line under "## Entries" is
  one dated entry - `YYYY-MM-DD · <where> · <topic> · <what happened>` - with <where> from
  the header's vocabulary, entries in date order (appended at the bottom, newest last), and
  never two entries on one line.

  The shape it catches: an append that did not end the previous line, so two entries share
  one line - one pass glued its entry onto the last one, and the retro counts entries by
  line, so the glued one reads as part of the entry above it. Also: an entry wrapped over
  two lines, a <where> outside the vocabulary, an entry inserted above newer ones, a date
  that is not a calendar date.

  A date-stamp quoted inside a parenthetical, a double-quoted string or a code span is prose
  (an entry may cite an earlier one), not a second entry, and is left alone.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'journal-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$rel = 'docs/process-journal.md'
$journal = Join-Path $RepoRoot $rel
if (-not (Test-Path -LiteralPath $journal)) { Write-Host "journal-lint: no $rel under $RepoRoot"; exit 2 }

# The vocabulary is the journal header's own list (## Format) - keep the two in step.
$where = 'review|brief|gate|consumer|release|infra|agent|manual'
$sep = ' · '
$entryRx = "^(?<date>\d{4}-\d{2}-\d{2})$sep(?<where>[a-z]+)$sep(?<topic>.+?)$sep(?<what>\S.*)$"
# A full entry stamp anywhere on a line: the shape a glued append leaves after the previous entry's text.
$stampRx = "\d{4}-\d{2}-\d{2}$sep(?:$where)$sep"

$lines = [System.IO.File]::ReadAllLines($journal)
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+Entries\s*$') { $start = $i + 1; break }
}
if ($start -lt 0) { Write-Host "journal-lint: FAIL - ${rel}: no '## Entries' heading"; exit 1 }

$problems = [System.Collections.Generic.List[string]]::new()
$entries = 0
$latest = [datetime]::MinValue
$inComment = $false
for ($i = $start; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $n = $i + 1
    # HTML comments (the retro marker) are not entries.
    if ($inComment) { if ($line -match '-->') { $inComment = $false }; continue }
    if ($line -match '^\s*<!--') { if ($line -notmatch '-->') { $inComment = $true }; continue }
    if ($line.Trim() -eq '') { continue }

    $m = [regex]::Match($line, $entryRx)
    if (-not $m.Success) {
        if ($line -match '^\d{4}-\d{2}-\d{2}') {
            $problems.Add("${rel}:${n}: not the entry shape 'YYYY-MM-DD · <where> · <topic> · <what happened>' (four fields, ' · ' between them)")
        }
        else {
            $problems.Add("${rel}:${n}: not an entry - an entry is one line starting with its date (a wrapped entry, or prose below the Entries heading)")
        }
        continue
    }
    $entries++

    $w = $m.Groups['where'].Value
    if ($w -notmatch "^(?:$where)$") {
        $problems.Add("${rel}:${n}: <where> is '$w'; the header's vocabulary is $($where -replace '\|', ', ')")
    }

    $date = $m.Groups['date'].Value
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($date, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        $problems.Add("${rel}:${n}: '$date' is not a calendar date")
    }
    elseif ($parsed -lt $latest) {
        $problems.Add("${rel}:${n}: dated $date below an entry dated $($latest.ToString('yyyy-MM-dd')) - entries are appended at the bottom, newest last")
    }
    else { $latest = $parsed }

    # A second stamp on the line is a glued append - unless it is quoted prose: inside an open
    # parenthesis, an open double quote or an open code span.
    foreach ($s in [regex]::Matches($line, $stampRx)) {
        if ($s.Index -eq 0) { continue }
        $prefix = $line.Substring(0, $s.Index)
        $openParens = [regex]::Matches($prefix, '\(').Count - [regex]::Matches($prefix, '\)').Count
        $quotes = [regex]::Matches($prefix, '"').Count
        $ticks = [regex]::Matches($prefix, '`').Count
        if ($openParens -gt 0 -or ($quotes % 2) -eq 1 -or ($ticks % 2) -eq 1) { continue }
        $problems.Add("${rel}:${n}: a second entry starts mid-line at column $($s.Index + 1) - the append did not end the previous line")
        break
    }
}

if ($problems.Count) {
    Write-Host "journal-lint: FAIL - $($problems.Count) issue(s) across $entries entries:"
    $problems | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "journal-lint: OK - $entries entries, one per line, dated in order"
exit 0
