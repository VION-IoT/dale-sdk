#requires -Version 7
# Self-test for journal-lint.ps1 (one dated entry per line under "## Entries", in order). Plain
# pwsh, NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/journal-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'journal-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("journallint-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'docs') | Out-Null
$journal = Join-Path $tmp 'docs/process-journal.md'

$header = @(
    '# Process journal',
    '',
    '## Format',
    '',
    'YYYY-MM-DD · <where> · <topic> · <what happened>',
    '',
    '## Entries',
    '',
    '<!-- retro-0 marker: everything below this line is unread by a retro. -->',
    ''
)
function Write-Journal([string[]]$entries) {
    [System.IO.File]::WriteAllLines($journal, [string[]]($header + $entries), [System.Text.UTF8Encoding]::new($false))
}
function Invoke-Lint {
    $script:out = & pwsh -NoProfile -File $lint -RepoRoot $tmp 2>&1 | Out-String
    return $LASTEXITCODE
}
function Expect([int]$code, [string]$case, [string]$mentions) {
    $rc = Invoke-Lint
    if ($rc -ne $code) { throw "$case expected exit $code, got $rc`n$script:out" }
    if ($mentions -and ($script:out -notmatch [regex]::Escape($mentions))) { throw "$case expected the output to mention '$mentions'`n$script:out" }
}

try {
    # Case 1: three entries in order, one citing an earlier entry's stamp in a parenthetical, one in a
    # code span, one in a double-quoted string; blank lines and the HTML marker between them -> 0
    Write-Journal @(
        '2026-08-12 · gate · cleanup-code.ps1 · `-Changed` saw tracked files only, so a new .cs was neither cleaned nor reported.',
        '',
        '2026-09-02 · review · GATE pass a1 · The mistake recurred (the first occurrence already has a journal line, 2026-09-02 · review · the insertion anchor) and writing it down did not change the behaviour.',
        '2026-09-03 · agent · SCEN pass a1 · The line `2026-09-03 · gate · x · y` is quoted here on purpose; so is "2026-09-03 · brief · x · y".'
    )
    Expect 0 'Case 1 (clean, with quoted stamps)' '3 entries'

    # Case 2: an append that did not end the previous line - two entries on one line -> 1
    Write-Journal @(
        '2026-09-02 · gate · #1 · First entry, ending with a full stop.2026-09-03 · agent · #2 · Second entry glued on.'
    )
    Expect 1 'Case 2 (glued entry)' 'second entry starts mid-line'

    # Case 3: an entry wrapped over two lines -> 1
    Write-Journal @(
        '2026-09-02 · gate · #1 · An entry whose author',
        'pressed return in the middle of it.'
    )
    Expect 1 'Case 3 (wrapped entry)' 'not an entry'

    # Case 4: a <where> outside the vocabulary -> 1
    Write-Journal @('2026-09-02 · shrug · #1 · A made-up category.')
    Expect 1 'Case 4 (unknown where)' "vocabulary"

    # Case 5: an entry inserted above a newer one -> 1
    Write-Journal @(
        '2026-09-03 · gate · #2 · Newer.',
        '2026-09-02 · gate · #1 · Older, appended after the newer one.'
    )
    Expect 1 'Case 5 (out of order)' 'newest last'

    # Case 6: three fields instead of four -> 1
    Write-Journal @('2026-09-02 · gate · only a topic, no what-happened')
    Expect 1 'Case 6 (missing field)' 'four fields'

    # Case 7: a date that is not a calendar date -> 1
    Write-Journal @('2026-13-40 · gate · #1 · The month does not exist.')
    Expect 1 'Case 7 (impossible date)' 'not a calendar date'

    # Case 8: no Entries heading at all -> 1
    [System.IO.File]::WriteAllLines($journal, [string[]]@('# Process journal', '', '2026-09-02 · gate · #1 · Entry.'), [System.Text.UTF8Encoding]::new($false))
    Expect 1 'Case 8 (no Entries heading)' 'Entries'

    Write-Host 'journal-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
