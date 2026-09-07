#requires -Version 7
<#
.SYNOPSIS
  Check that a disposition table covers every entry in the finding ledger
  (`docs/specs/_findings.md`), and list the ledger with each entry's bucket and reason.

  Exit 1 when a ledger entry has no disposition row, when a row carries a bucket outside the four
  (`fix-now`, `decision`, `Jira`, `leave`), when a row's reason is empty, when two rows name the
  same entry, when two entries share one bold lead, or when a row files an entry under an area the
  ledger does not put it in. Plus the anti-vacuous floors: zero entries parsed, or zero rows parsed.

.DESCRIPTION
  The ledger's own header sends it to the retro to be triaged in bulk; a triage that quietly
  skips entries is the failure this catches, and reading 75 prose bullets against a table by eye
  is exactly the check a human does badly. Entries are keyed on their **bold lead text**, not on
  their position: a triage outlives the deletions it causes, and positions shift the moment one
  entry is fixed and deleted.

  A row whose entry is no longer in the ledger is **resolved**, not a failure - `_findings.md`
  says an entry that is fixed is deleted with the PR that fixes it, so a disposition round's rows
  outlive their entries on purpose. The resolved count is printed, which is what makes the table
  readable as progress through a round rather than only as a snapshot of one.

  Both tallies are printed, not just floored, and printed on the failing branch as well as the
  passing one: a floor only catches a count reaching zero, and the way a parse breaks in practice
  is partial - one changed heading takes an area's entries out of the scan while the total stays
  healthy and every row still matches something.

  This is the triage tool, not a standing gate, so it is not wired into `spec-gates.yml`: it needs
  a disposition table to check against, and there is one only while a triage round is open. What IS
  in CI is its self-test, through `run-script-tests.ps1`, and case 13 there runs this with no
  parameters against the repo's own two files - so the DEFAULTS are gated, the table's coverage is
  not. That distinction is the whole of the case: a lane that records a finding without touching
  this round's table would otherwise fail an unrelated PR on a table it has nothing to do with,
  which is not what a triage tool gets to do.

  -Dispositions defaults to the round that is open now. When that change doc archives, this stops
  resolving and case 13 goes red - deliberately, because at that point the choice is to re-point
  the default at the next round's table or delete this script with the round. A default naming a
  file under `docs/changes/archive/` is a tool checking a table nobody can act on.

.EXAMPLE
  pwsh -File scripts/ledger-buckets.ps1
  pwsh -File scripts/ledger-buckets.ps1 -List      # one Markdown row per entry, to seed a table
#>
[CmdletBinding()]
param(
    [string]$Ledger,
    [string]$Dispositions,
    [switch]$List
)
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Ledger) { $Ledger = Join-Path $repoRoot 'docs/specs/_findings.md' }
if (-not $Dispositions) { $Dispositions = Join-Path $repoRoot 'docs/changes/2026-09-07-sdd-closeout.md' }

foreach ($p in @($Ledger, $Dispositions)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Host "ledger-buckets: FAIL - no such file: $p"; exit 2 }
}

$buckets = @('fix-now', 'decision', 'Jira', 'leave')

# The key is the bold lead with its markup and punctuation struck, so a row survives a wrapped
# title being rewrapped, a backtick moving, or the strikethrough on `~~Four~~ Three shipped …`.
function Get-Key([string]$title) {
    return (($title -replace '[^A-Za-z0-9]+', ' ').Trim().ToLowerInvariant())
}

# --- the ledger: one entry per top-level bullet, its title the bold lead ---------------------
# The bold lead wraps across lines in 7 of the 75 entries, so the title is matched over the
# entry's joined text and never over the bullet's first line alone.
$entries = [System.Collections.Generic.List[object]]::new()
$area = ''
$current = $null
$areas = [System.Collections.Generic.HashSet[string]]::new()
$malformed = [System.Collections.Generic.List[string]]::new()
$entryKeys = [System.Collections.Generic.HashSet[string]]::new()
function Complete-Entry() {
    if (-not $script:current) { return }
    if ($script:current.Text -match '^- \*\*(.+?)\*\*') {
        $script:current.Title = $Matches[1]
        $script:current.Key = Get-Key $Matches[1]
        # Two entries sharing a lead both map to one row, so neither is reported missing and the
        # count of rows still looks right - the report would print 76 entries against 75 rows and
        # call it OK. The key is the whole of the coverage check, so a collision in it is the one
        # way an entry goes unbucketed with nothing saying so.
        if (-not $script:entryKeys.Add($script:current.Key)) {
            $script:malformed.Add("$($script:current.Area) `"$($script:current.Title)`": two ledger entries share this bold lead, so one disposition row answers both")
        }
        $script:entries.Add($script:current)
    }
    else {
        $script:malformed.Add("$($script:current.Area) `"$($script:current.Text.Substring(0, [Math]::Min(60, $script:current.Text.Length)))`": entry has no **bold lead** to key on")
    }
    $script:current = $null
}
foreach ($line in (Get-Content -LiteralPath $Ledger)) {
    if ($line -match '^## `([A-Za-z]+)`') { Complete-Entry; $area = $Matches[1]; $areas.Add($area) | Out-Null; continue }
    if ($line -match '^- ') {
        Complete-Entry
        $current = [pscustomobject]@{ Area = $area; Text = $line; Title = ''; Key = '' }
        continue
    }
    if ($current -and $line -match '^\s+\S') { $current.Text += ' ' + $line.Trim() }
}
Complete-Entry

# --- the dispositions: the Markdown table under "### Ledger dispositions" --------------------
$rows = [System.Collections.Generic.List[object]]::new()
$inSection = $false
foreach ($line in (Get-Content -LiteralPath $Dispositions)) {
    if ($line -match '^#{2,3} ') { $inSection = $line -match '^### Ledger dispositions\b'; continue }
    if (-not $inSection) { continue }
    if ($line -notmatch '^\|') { continue }
    $cells = @(($line.Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -lt 4) { continue }
    # The colons are Markdown's column alignment; a prettifier adds them, and read as data they
    # become a row whose bucket is ':---:'.
    if ($cells[0] -match '^:?-{2,}:?$' -or $cells[0] -eq 'Area') { continue }
    $rows.Add([pscustomobject]@{
            Area   = ($cells[0] -replace '`', '')
            Title  = $cells[1]
            Key    = Get-Key $cells[1]
            Bucket = ($cells[2] -replace '`', '')
            Why    = $cells[3]
        })
}

# Anti-vacuous floors, bom-lint's shape: a parse that reaches nothing reports the same "every entry
# is dispositioned" as a parse that is silently broken - and this one has two inputs, so a floor on
# the ledger alone would pass with an empty table and a floor on the table alone with an empty ledger.
if ($entries.Count -eq 0) {
    Write-Host "ledger-buckets: FAIL - ZERO ledger entry(ies) parsed from $Ledger (nothing was checked - anti-vacuous floor)."
    exit 1
}

$byKey = @{}
$problems = [System.Collections.Generic.List[string]]::new()
$problems.AddRange($malformed)
foreach ($r in $rows) {
    if ($byKey.ContainsKey($r.Key)) {
        $problems.Add("$($r.Area) `"$($r.Title)`": two disposition rows name this entry")
        continue
    }
    $byKey[$r.Key] = $r
    if ($buckets -notcontains $r.Bucket) {
        $problems.Add("$($r.Area) `"$($r.Title)`": bucket '$($r.Bucket)' is not one of $($buckets -join ', ')")
    }
    if (-not $r.Why) {
        $problems.Add("$($r.Area) `"$($r.Title)`": no reason given - a bucket without one is a vote, not a disposition")
    }
}

if ($List) {
    foreach ($e in $entries) {
        $r = $byKey[$e.Key]
        $bucket = if ($r) { $r.Bucket } else { '?' }
        $why = if ($r) { $r.Why } else { '' }
        Write-Host "| ``$($e.Area)`` | $($e.Title) | $bucket | $why |"
    }
    # The resolved rows too, or a re-seed after a batch of fixes silently deletes the record of
    # them: their entries are gone from the ledger, and a listing that walks entries alone would
    # hand back a table shorter by exactly the work that was done.
    foreach ($r in $rows) {
        if ($entryKeys.Contains($r.Key)) { continue }
        Write-Host "| ``$($r.Area)`` | $($r.Title) | $($r.Bucket) | $($r.Why) |"
    }
}

# The second floor sits below the listing on purpose: seeding a table means running -List against a
# section that has none yet, and a floor above it would refuse the one run that needs no table.
if ($rows.Count -eq 0) {
    Write-Host "ledger-buckets: FAIL - ZERO disposition row(s) parsed from $Dispositions across $($entries.Count) ledger entry(ies) (nothing was checked - anti-vacuous floor)."
    exit 1
}

$resolved = 0
$seen = @{}
foreach ($e in $entries) {
    $seen[$e.Key] = $true
    $r = $byKey[$e.Key]
    if (-not $r) {
        $problems.Add("$($e.Area) `"$($e.Title)`": no disposition row - every ledger entry carries a bucket and a reason")
        continue
    }
    if ($r.Area -ne $e.Area) {
        $problems.Add("$($e.Area) `"$($e.Title)`": disposition row files it under $($r.Area)")
    }
}
foreach ($r in $rows) { if (-not $seen.ContainsKey($r.Key)) { $resolved++ } }

$byBucket = ($buckets | ForEach-Object { $b = $_; "$b $(@($rows | Where-Object { $_.Bucket -eq $b }).Count)" }) -join ', '
$tally = "$($entries.Count) entry(ies) across $($areas.Count) area(s); $($rows.Count) row(s), $resolved resolved ($byBucket)"
if ($problems.Count) {
    Write-Host "ledger-buckets: FAIL - $($problems.Count) problem(s) over $tally`:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "ledger-buckets: OK - $tally"
exit 0
