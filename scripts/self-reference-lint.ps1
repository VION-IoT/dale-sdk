#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a Markdown table cell is used as a pointer and points at itself: `(this PR)`,
  `this commit`, `this branch`, `the current PR`. A table cell in these docs is where an
  identifier lives - a PR number, a session name, a commit - and a self-reference there is
  read after the merge that made it resolvable, by which time it silently names whatever PR
  the reader is holding.

  Cells only, and only a cell that IS the pointer: strip emphasis, backticks, brackets and
  surrounding punctuation, and what is left must be the phrase and nothing else. Prose is never
  judged, and neither is a cell that merely mentions one. An in-flight change doc narrating
  "this PR touches scripts/" is describing the work, is true while it is written, and reads as
  history afterwards; a definition cell reading "the .cs this branch touched" is a description;
  a row whose PR column says "(this PR)" is a pointer that was never filled in.

  Whole-cell is the rule because the obvious alternative - a length bound separating a token
  from a paragraph - cannot be pinned. A bound of 60 survived being doubled to 130 with every
  self-test still green, which makes the number an assertion rather than a rule (P5).

  What that gives up, stated rather than discovered later: a cell that carries the phrase inside
  a sentence is NOT caught. `landed in this PR`, `see this PR`, `this PR (#TBD)` and `PR: this PR`
  all pass, and the contains-plus-length rule caught them. The trade is deliberate - that rule also
  failed a legitimate description cell (`the .cs this branch touched`), and a gate that reddens a
  docs PR over a description costs more than one that misses a sentence. The founding shape, a bare
  `(this PR)` in an Implementation state column, is what this catches and what shipped twice.

  The shape it catches: `T-013` journalled two Implementation state rows still reading
  "(this PR)" after their PRs had merged, wrote the rule "write the row with a number", and
  `T-014` shipped the identical cell one commit later - caught by its review round, not by any
  gate. Retro-1 counted the family (a pointer with no durable referent) at 19 lines across four
  slices of its window.

  Out of scope, by repo-root-relative path: history and append-only logs (`docs/retro/`,
  `docs/changes/archive/`), the generated snapshots, and the journal - each is written once and
  never revised, so a self-reference in them is a fact about its own moment.

  Scans the tracked files (`git ls-files`); with -RepoRoot outside a git repo it walks the tree,
  skipping bin/, obj/, node_modules/ and .git/ (the self-test's path).
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'self-reference-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

# Written once and never revised: a self-reference there names its own moment correctly.
$outOfScopeRx = '^docs/(retro|changes/archive|snapshots)/|^docs/process-journal\.md$|(^|/)(bin|obj|node_modules|\.git)/'
# The cell must BE the pointer. Markdown decoration and the punctuation a pointer is written
# inside are stripped first, so `**(this PR)**` and `(this PR)` reduce to the same phrase and a
# cell that merely mentions one does not reduce to anything.
$decorationRx = '^[\s`*_~\[\]()<>"''.,;:!?-]+|[\s`*_~\[\]()<>"''.,;:!?-]+$'
$selfRefRx = '(?i)^(?:this|the\s+current)\s+(?:PR|pull\s+request|commit|branch)$'

$files = @()
$inGit = $false
Push-Location $RepoRoot
try {
    $top = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top -and ([System.IO.Path]::GetFullPath($top.Trim()).TrimEnd('\', '/') -eq $RepoRoot.TrimEnd('\', '/'))) {
        $inGit = $true
        $files = @(git ls-files -z | ForEach-Object { $_ }) -split "`0" | Where-Object { $_ } | ForEach-Object { Join-Path $RepoRoot $_ }
    }
}
finally { Pop-Location }
if (-not $inGit) {
    $files = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
}

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
$cells = 0
foreach ($path in $files) {
    if ([System.IO.Path]::GetExtension($path).ToLowerInvariant() -ne '.md') { continue }
    $rel = ($path.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    if ($rel -match $outOfScopeRx) { continue }
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $checked++
    $lines = [System.IO.File]::ReadAllLines($path)
    $inFence = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        # A table row: starts with a pipe. The separator row carries no prose.
        if ($line -notmatch '^\s*\|') { continue }
        if ($line -match '^\s*\|[\s:|-]*\|\s*$') { continue }
        $n = $i + 1
        # Split on unescaped pipes; drop the empty leading and trailing fields.
        $fields = [regex]::Split($line.Trim(), '(?<!\\)\|')
        for ($f = 1; $f -lt $fields.Count - 1; $f++) {
            $cell = $fields[$f].Trim()
            if (-not $cell) { continue }
            $cells++
            $bare = [regex]::Replace($cell, $decorationRx, '')
            if ($bare -match $selfRefRx) {
                $problems.Add("${rel}:${n}: a table cell points at itself - '$cell'. Write the identifier (the PR number, the sha, the branch); the phrase is read after the merge that would have resolved it.")
            }
        }
    }
}

if ($problems.Count) {
    Write-Host "self-reference-lint: FAIL - $($problems.Count) self-referential cell(s) across $checked file(s):"
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# Anti-vacuous floor, as bom-lint and pragma-reason-lint carry: a scan that reached nothing
# reports that everything conformed, which is the same output as a scan that is silently broken.
if ($checked -eq 0) {
    Write-Host "self-reference-lint: FAIL - the scan reached 0 markdown file(s); this repository has many, so the walk or the scope filter is broken"
    exit 1
}
Write-Host "self-reference-lint: OK - $cells table cell(s) across $checked markdown file(s), none self-referential"
exit 0
