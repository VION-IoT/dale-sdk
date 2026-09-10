#requires -Version 7
# Self-test for self-reference-lint.ps1 (a Markdown table cell used as a pointer must not point at
# itself). Plain pwsh, NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/self-reference-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'self-reference-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("selfreflint-" + [guid]::NewGuid().ToString('N'))

function Write-Doc([string]$relPath, [string[]]$lines) {
    $full = Join-Path $tmp $relPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
    [System.IO.File]::WriteAllLines($full, [string[]]$lines, [System.Text.UTF8Encoding]::new($false))
}
function Reset-Tree {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
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

$tableHead = @('| Task | Status | PR |', '| --- | --- | --- |')

try {
    # Case 1: a row whose PR cell carries a number -> 0. The shape the rule wants.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' ($tableHead + @('| `T-013` | done | #208 |'))
    Expect 0 'Case 1 (numbered row)' 'none self-referential'

    # Case 2: the T-013/T-014 defect verbatim -> 1
    Reset-Tree
    Write-Doc 'docs/changes/x.md' ($tableHead + @('| `T-014` | done | (this PR) |'))
    Expect 1 'Case 2 (the merged defect)' 'points at itself'

    # Case 3: prose saying "this PR" is never judged - only cells are.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' @(
        'This PR touches `scripts/` and `docs/`, so the gate suite runs.',
        '- `T-001` *(this PR, the `sdk: sdd big picture` session)* — the handover.'
    )
    Expect 0 'Case 3 (prose is not a cell)' 'none self-referential'

    # Case 4: a long narrative cell is a paragraph, not a pointer -> 0. The length bound is the
    # whole defence against false positives, so it is pinned here.
    Reset-Tree
    $long = 'the round re-ran every gate after the rebase, and this PR carries the pasted output for each of them, including the two that had to be re-run'
    Write-Doc 'docs/changes/x.md' ($tableHead + @("| Gates | green | $long |"))
    Expect 0 'Case 4 (narrative cell)' 'none self-referential'

    # Case 5: the other three phrasings, plus "the current PR" -> 1 each, four findings on four rows.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' ($tableHead + @(
            '| a | done | this commit |',
            '| b | done | this branch |',
            '| c | done | the current PR |',
            '| d | done | This Pull Request |'
        ))
    Expect 1 'Case 5 (every phrasing)' '4 self-referential cell(s)'

    # Case 6: the append-only logs and history are out of scope -> 0, even carrying the exact defect.
    Reset-Tree
    Write-Doc 'docs/changes/archive/old-pass.md' ($tableHead + @('| a | done | (this PR) |'))
    Write-Doc 'docs/retro/note.md' ($tableHead + @('| a | done | (this PR) |'))
    Write-Doc 'docs/rfcs/0001.md' ($tableHead + @('| a | done | (this PR) |'))
    Write-Doc 'docs/snapshots/s.md' ($tableHead + @('| a | done | (this PR) |'))
    Write-Doc 'docs/process-journal.md' ($tableHead + @('| a | done | (this PR) |'))
    Expect 0 'Case 6 (history out of scope)' 'none self-referential'

    # Case 7: a fenced block that looks like a table is code, not a table -> 0.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' @('```', '| a | done | (this PR) |', '```')
    Expect 0 'Case 7 (fenced)' 'none self-referential'

    # Case 8: the separator row is skipped and the leading/trailing empty fields are not cells, so a
    # one-column table still finds its single cell -> 1.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' @('| PR |', '| :--- |', '| (this PR) |')
    Expect 1 'Case 8 (single column)' 'points at itself'

    # Case 9: a non-Markdown file carrying the phrase in a pipe-delimited line -> 0.
    Reset-Tree
    Write-Doc 'scripts/x.ps1' @('| a | done | (this PR) |')
    Expect 0 'Case 9 (not markdown)' 'none self-referential'

    # Case 10: "this PRs" and "commitment" must not match - the word boundary is load-bearing.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' ($tableHead + @('| a | done | this PRs of theirs |', '| b | done | this commitment |'))
    Expect 0 'Case 10 (word boundary)' 'none self-referential'

    # Case 11: the separator row is not cells. It can never carry the phrase (it has no letters), so
    # the skip cannot change the verdict - it changes the count the OK line reports, and that is what
    # this pins. Head 3 + row 3 = 6, not 9.
    Reset-Tree
    Write-Doc 'docs/changes/x.md' ($tableHead + @('| `T-013` | done | #208 |'))
    Expect 0 'Case 11 (separator is not cells)' '6 table cell(s)'

    Write-Host 'self-reference-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
