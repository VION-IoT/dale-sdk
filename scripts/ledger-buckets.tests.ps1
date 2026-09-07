#requires -Version 7
# Self-test for ledger-buckets.ps1 (every finding-ledger entry carries a bucket and a reason).
# Plain pwsh, NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/ledger-buckets.tests.ps1`.
#
# Cases assert the MESSAGE and the COUNTS, not only the exit code. Six different defects exit 1,
# so an exit-code-only case cannot tell an unbucketed entry from a misfiled one; and no exit code
# can see the resolved tally, or a whole area dropping out of the parse while every remaining row
# still matches something. Every number in the report is pinned by two cases that disagree about
# it - one case per number reads the same against a script printing the constant.
#
# Case 13 runs the script with NO parameters, against this repo's own ledger and change doc: the
# fixture cases all pass explicit paths, so without it the default-path resolution - the only way
# anyone actually invokes this - would be the one branch no case reaches.
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'ledger-buckets.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ledgerbuckets-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$ledger = Join-Path $tmp '_findings.md'
$doc = Join-Path $tmp 'change-doc.md'

# Shaped like docs/specs/_findings.md: a prose header, `AREA` headings in backticks, one top-level
# bullet per entry with a bold lead, indented continuation lines, and a bold lead that WRAPS - the
# shape 7 of the 75 real entries have, and the one a parser reading the bullet's first line misses.
$ledgerBody = @'
# Finding ledger

Defects found and deliberately **not** fixed where they were found.

## `AAA` — the first area (2026-09-02)

- **A first finding.** Its body runs on
  over an indented line. *(AAA pass row 1 — `AAA`.)*
- **A second finding whose bold lead wraps across the line break the way seven of the real
  ones do.** Body. *(AAA pass row 2 — `BBB`.)*

## `BBB` — the second area (2026-09-04)

- **A third finding with `backticks` and a ~~struck~~ word.** Body. *(BBB pass row 3 — `BBB`.)*
'@

function Set-Doc([string]$rowsBlock) {
    # Shaped like the change doc: a table BEFORE the section and one AFTER it, both of which the
    # section parse has to ignore, or the check reads four-column rows that are not dispositions.
    $body = @"
# A change doc

## Full design

### Implementation state

| Task | Status | Session | PR |
|---|---|---|---|
| ``T-006`` | done | ``sdk: sdd closeout T-006`` | #196 |

### Ledger dispositions

| Area | Entry | Bucket | Why |
|---|---|---|---|
$rowsBlock

### Skill retirement — where every rule went

| Rule | Owner | Bucket | Why |
|---|---|---|---|
| A rule | a file | not-a-bucket | not a disposition |
"@
    Set-Content -LiteralPath $doc -Value $body -NoNewline
}

$rowA = '| `AAA` | A first finding. | fix-now | small, one file, no decision. |'
$rowB = '| `AAA` | A second finding whose bold lead wraps across the line break the way seven of the real ones do. | leave | a decision is open. |'
$rowC = '| `BBB` | A third finding with `backticks` and a ~~struck~~ word. | Jira | filed under VION-16. |'

$script:Output = ''
function Invoke-Check([string[]]$extra) {
    $argv = @('-NoProfile', '-File', $script, '-Ledger', $ledger, '-Dispositions', $doc) + $extra
    $script:Output = (pwsh @argv) -join "`n"
    return $LASTEXITCODE
}
function Assert-Says($fragment, $case) {
    if ($script:Output -notmatch [regex]::Escape($fragment)) {
        throw "$case expected the report to say '$fragment'; it said:`n$script:Output"
    }
}
function Assert-Silent($fragment, $case) {
    if ($script:Output -match [regex]::Escape($fragment)) {
        throw "$case expected the report NOT to say '$fragment'; it said:`n$script:Output"
    }
}

try {
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 1: every entry dispositioned. The wrapped lead (row B) and the marked-up one (row C) both
    # match, which is the whole of the keying rule: strip the markup, key on the words.
    Set-Doc ($rowA, $rowB, $rowC -join "`n")
    if ((Invoke-Check @()) -ne 0) { throw "Case 1 (all three dispositioned) expected 0" }
    Assert-Says 'ledger-buckets: OK - 3 entry(ies) across 2 area(s); 3 row(s), 0 resolved (fix-now 1, decision 0, Jira 1, leave 1)' 'Case 1'

    # Case 1b: the same assertion with different answers. Case 1 alone cannot tell a script that
    # counts from one printing its numbers as literals, and the tables before and after the section
    # are what a leaking parse would add to the row count.
    Set-Content -LiteralPath $ledger -Value ($ledgerBody + "`n- **A fourth finding.** Body.`n") -NoNewline
    Set-Doc (($rowA, $rowB, $rowC, '| `BBB` | A fourth finding. | leave | no observable. |') -join "`n")
    if ((Invoke-Check @()) -ne 0) { throw "Case 1b (a fourth entry and row) expected 0" }
    Assert-Says 'ledger-buckets: OK - 4 entry(ies) across 2 area(s); 4 row(s), 0 resolved (fix-now 1, decision 0, Jira 1, leave 2)' 'Case 1b'
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 2: an entry with no row. The defect the check exists for - a triage that skipped one -
    # and the entry is named, so the fix is one jump away.
    Set-Doc ($rowA, $rowC -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 2 (an unbucketed entry) expected 1" }
    Assert-Says 'no disposition row - every ledger entry carries a bucket and a reason' 'Case 2'
    Assert-Says 'A second finding whose bold lead wraps' 'Case 2'
    # The FAIL header carries the same tallies as the OK line. Without this, a mutation of the
    # numbers on the failing branch has no case watching it at all.
    Assert-Says 'FAIL - 1 problem(s) over 3 entry(ies) across 2 area(s); 2 row(s), 0 resolved (fix-now 1, decision 0, Jira 1, leave 0)' 'Case 2'

    # Case 2b: two entries with no row. With only ever one problem in the suite, a script printing
    # the constant 1 in its header reads identically.
    Set-Doc $rowA
    if ((Invoke-Check @()) -ne 1) { throw "Case 2b (two unbucketed entries) expected 1" }
    Assert-Says 'FAIL - 2 problem(s) over 3 entry(ies) across 2 area(s); 1 row(s), 0 resolved (fix-now 1, decision 0, Jira 0, leave 0)' 'Case 2b'

    # Case 3: a bucket outside the four. The four are the vocabulary T-007, T-008 and T-009 consume;
    # a fifth one silently routes an entry to nobody.
    Set-Doc (($rowA, ($rowB -replace 'leave', 'later'), $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 3 (unknown bucket) expected 1" }
    Assert-Says "bucket 'later' is not one of fix-now, decision, Jira, leave" 'Case 3'

    # Case 4: a bucket with no reason. A bucket on its own is a vote; the reason is what the operator
    # rules on. Asserted on the message, not the exit code, which case 3 also produces.
    Set-Doc (($rowA, ($rowB -replace 'a decision is open\.', ''), $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 4 (empty reason) expected 1" }
    Assert-Says 'no reason given - a bucket without one is a vote, not a disposition' 'Case 4'

    # Case 5: two rows naming one entry. They disagree or they do not; either way one of them is
    # unread, and a duplicate is what a hand-merged table produces.
    Set-Doc (($rowA, ($rowA -replace 'fix-now', 'leave'), $rowB, $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 5 (duplicate row) expected 1" }
    Assert-Says 'two disposition rows name this entry' 'Case 5'

    # Case 6: a row filing an entry under an area the ledger does not put it in. The area column is
    # how a later task reads the table by area, so a wrong one sends the work to the wrong session.
    Set-Doc (($rowA, ($rowB -replace '`AAA`', '`BBB`'), $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 6 (misfiled area) expected 1" }
    Assert-Says 'disposition row files it under BBB' 'Case 6'

    # Case 7: a row whose entry is gone is RESOLVED, not a failure - the ledger deletes an entry when
    # its fix lands, so a round's rows outlive their entries by design.
    Set-Doc (($rowA, $rowB, $rowC, '| `BBB` | A finding that has since been fixed and deleted. | fix-now | done in T-007. |') -join "`n")
    if ((Invoke-Check @()) -ne 0) { throw "Case 7 (a resolved row) expected 0" }
    Assert-Says '3 entry(ies) across 2 area(s); 4 row(s), 1 resolved (fix-now 2, decision 0, Jira 1, leave 1)' 'Case 7'
    Assert-Silent 'no disposition row' 'Case 7'

    # Case 7b: a second resolved row. One resolved row cannot separate a count from the constant 1,
    # and this is the tally that reads as progress through a round.
    Set-Doc (($rowA, $rowB, $rowC,
            '| `BBB` | A finding that has since been fixed and deleted. | fix-now | done in T-007. |',
            '| `BBB` | Another finding since fixed. | decision | done in T-008. |') -join "`n")
    if ((Invoke-Check @()) -ne 0) { throw "Case 7b (two resolved rows) expected 0" }
    Assert-Says '3 entry(ies) across 2 area(s); 5 row(s), 2 resolved (fix-now 2, decision 1, Jira 1, leave 1)' 'Case 7b'

    # Case 8: the key survives the markup being written differently in the table than in the ledger.
    # A row copied by hand loses a backtick or a strikethrough; keying on the raw text would report
    # the entry unbucketed and the row resolved at the same time, which is the confusing pair.
    Set-Doc (($rowA, $rowB, '| `BBB` | A third finding with backticks and a struck word. | Jira | filed under VION-16. |') -join "`n")
    if ((Invoke-Check @()) -ne 0) { throw "Case 8 (markup differs between ledger and table) expected 0" }
    Assert-Says '3 entry(ies) across 2 area(s); 3 row(s), 0 resolved' 'Case 8'

    # Case 9: an area heading that stops parsing as one. Both floors hold - entries and rows are
    # non-zero - and every row still matches an entry, so nothing but the printed AREA tally shows
    # that a whole section has been swallowed into the one above it. Case 1 says two areas over the
    # same three entries; between them the area count is pinned against a script printing a literal.
    Set-Content -LiteralPath $ledger -Value ($ledgerBody -replace '## `BBB`', '## BBB') -NoNewline
    Set-Doc (($rowA, $rowB, $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 9 (an area heading out of shape) expected 1" }
    Assert-Says '3 entry(ies) across 1 area(s); 3 row(s), 0 resolved' 'Case 9'
    Assert-Says 'disposition row files it under BBB' 'Case 9'
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 10: the floor for a ledger the parse reaches nothing in. A broken parse reports the same
    # "every entry is dispositioned" as a ledger that is genuinely covered.
    Set-Content -LiteralPath $ledger -Value "# Finding ledger`n`nNo entries yet.`n" -NoNewline
    if ((Invoke-Check @()) -ne 1) { throw "Case 10 (no ledger entries) expected 1" }
    Assert-Says 'ZERO ledger entry(ies) parsed' 'Case 10'
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 11: the second floor. One floor on the ledger alone passes an empty table, which is the
    # state this section is in before a triage runs - and the state it returns to if the heading is
    # renamed under the parse's feet.
    Set-Doc ''
    if ((Invoke-Check @()) -ne 1) { throw "Case 11 (no disposition rows) expected 1" }
    Assert-Says 'ZERO disposition row(s) parsed' 'Case 11'

    # Case 12: -List seeds a table from a section that has none. It runs BELOW the entry floor and
    # ABOVE the row floor, so the one run that legitimately has no table still prints; the marker is
    # `?`, which is what an unbucketed entry looks like in the seed.
    if ((Invoke-Check @('-List')) -ne 1) { throw "Case 12 (-List with no table) expected 1" }
    Assert-Says '| `AAA` | A first finding. | ? |  |' 'Case 12'
    Assert-Says 'ZERO disposition row(s) parsed' 'Case 12'

    # Case 12b: -List against a filled table carries the bucket and the reason through, so the seed
    # and the check read the same table rather than two hand-kept copies.
    Set-Doc (($rowA, $rowB, $rowC) -join "`n")
    if ((Invoke-Check @('-List')) -ne 0) { throw "Case 12b (-List with a full table) expected 0" }
    Assert-Says '| `AAA` | A first finding. | fix-now | small, one file, no decision. |' 'Case 12b'
    Assert-Silent '| ? |' 'Case 12b'

    # Case 14: a ledger bullet with no bold lead. It cannot be keyed, so it cannot be dispositioned -
    # and dropping it from the parse is the silent shape: the entry tally falls by one while every
    # remaining row still matches, which is a finding with no bucket and nothing saying so.
    Set-Content -LiteralPath $ledger -Value ($ledgerBody + "`n- A finding written without a bold lead.`n") -NoNewline
    Set-Doc (($rowA, $rowB, $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 14 (a bullet with no bold lead) expected 1" }
    Assert-Says 'entry has no **bold lead** to key on' 'Case 14'
    Assert-Says '3 entry(ies) across 2 area(s); 3 row(s), 0 resolved' 'Case 14'
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 15: a ledger with two entries under one bold lead. Both map to the one row, so neither is
    # reported missing and the row count still looks right - the report prints its own evidence (one
    # more entry than rows, nothing resolved) and would otherwise call it OK. Latent, not live: the
    # 75 real entries have 75 distinct keys.
    Set-Content -LiteralPath $ledger -Value ($ledgerBody + "`n- **A first finding.** A different body under a lead that is already taken.`n") -NoNewline
    Set-Doc (($rowA, $rowB, $rowC) -join "`n")
    if ((Invoke-Check @()) -ne 1) { throw "Case 15 (two entries sharing a bold lead) expected 1" }
    Assert-Says 'two ledger entries share this bold lead, so one disposition row answers both' 'Case 15'
    Assert-Says '4 entry(ies) across 2 area(s); 3 row(s), 0 resolved' 'Case 15'
    Set-Content -LiteralPath $ledger -Value $ledgerBody -NoNewline

    # Case 16: a separator row written with Markdown's alignment colons. Every prettifier emits them,
    # and read as data the separator becomes a row whose bucket is ':---:' - a FAIL whose message is
    # about the table's punctuation rather than about any disposition.
    Set-Doc (($rowA, $rowB, $rowC) -join "`n")
    (Get-Content -LiteralPath $doc -Raw).Replace("| Area | Entry | Bucket | Why |`n|---|---|---|---|", "| Area | Entry | Bucket | Why |`n|:---|:---|:---:|:---|") |
        Set-Content -LiteralPath $doc -NoNewline
    if ((Invoke-Check @()) -ne 0) { throw "Case 16 (aligned separator row) expected 0" }
    Assert-Says '3 entry(ies) across 2 area(s); 3 row(s), 0 resolved' 'Case 16'

    # Case 17: -List carries the resolved rows too. A re-seed after a batch of fixes would otherwise
    # hand back a table shorter by exactly the work that was done, since those entries are gone from
    # the ledger by then.
    Set-Doc (($rowA, $rowB, $rowC, '| `BBB` | A finding that has since been fixed and deleted. | fix-now | done in T-007. |') -join "`n")
    if ((Invoke-Check @('-List')) -ne 0) { throw "Case 17 (-List with a resolved row) expected 0" }
    Assert-Says '| `BBB` | A finding that has since been fixed and deleted. | fix-now | done in T-007. |' 'Case 17'

    # Case 18: a path that does not exist. This is the archive scenario - the default -Dispositions
    # names the open round's change doc, and archiving moves it - so the exit code has to say "I
    # could not read that", not "the table is fine".
    $script:Output = (pwsh -NoProfile -File $script -Ledger $ledger -Dispositions (Join-Path $tmp 'gone.md')) -join "`n"
    if ($LASTEXITCODE -ne 2) { throw "Case 18 (a missing input file) expected 2; it said:`n$script:Output" }
    Assert-Says 'no such file' 'Case 18'

    # Case 13: the invocation production takes. Every case above passes -Ledger and -Dispositions, so
    # the default-path resolution - the repo's own ledger and the open round's change doc - is
    # reached by none of them.
    #
    # It asserts that the defaults RESOLVE and the parse reaches both files, and deliberately accepts
    # FAIL as well as OK. run-script-tests is step 1 of spec-gates.yml, so anything this case demands
    # is demanded of every PR in the repo - and `_findings.md`'s header invites any lane to add an
    # entry. Requiring OK here would fail an unrelated PR on a disposition table it never touched.
    # What stays gated is the pair of default paths: when the change doc archives, this goes red, and
    # that is the reminder to re-point it or delete the script with the round.
    $script:Output = (pwsh -NoProfile -File $script) -join "`n"
    $rc = $LASTEXITCODE
    if ($rc -ne 0 -and $rc -ne 1) { throw "Case 13 (the repo's own files, no parameters) expected 0 or 1, got $rc; it said:`n$script:Output" }
    if ($script:Output -notmatch 'ledger-buckets: (OK|FAIL) - .*\d+ entry\(ies\) across \d+ area\(s\); \d+ row\(s\), \d+ resolved \(fix-now \d+, decision \d+, Jira \d+, leave \d+\)') {
        throw "Case 13 expected the default run to reach both files and report a full tally; it said:`n$script:Output"
    }

    Write-Host 'ledger-buckets.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
