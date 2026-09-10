#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a Markdown table cell is used as a pointer and points at itself: `(this PR)`,
  `this commit`, `this branch`, `the current PR`. A table cell in these docs is where an
  identifier lives - a PR number, a session name, a commit - and a self-reference there is
  read after the merge that made it resolvable, by which time it silently names whatever PR
  the reader is holding.

  Cells only, and short ones: the whole trimmed cell must be at most 60 characters. Prose is
  never judged. An in-flight change doc narrating "this PR touches scripts/" is describing the
  work, is true while it is written, and reads as history afterwards; a row whose PR column
  says "(this PR)" is a pointer that was never filled in. The length bound is what keeps the
  two apart - a narrative cell is a paragraph, a pointer cell is a token.

  The shape it catches: `T-013` journalled two Implementation state rows still reading
  "(this PR)" after their PRs had merged, wrote the rule "write the row with a number", and
  `T-014` shipped the identical cell one commit later - caught by its review round, not by any
  gate. Retro-1 counted the family (a pointer with no durable referent) at 19 lines across four
  slices of its window.

  Out of scope, by repo-root-relative path: history and append-only logs (`docs/rfcs/`,
  `docs/retro/`, `docs/changes/archive/`), the generated snapshots, and the journal - each is
  written once and never revised, so a self-reference in them is a fact about its own moment.

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
$outOfScopeRx = '^docs/(rfcs|retro|changes/archive|snapshots)/|^docs/process-journal\.md$|(^|/)(bin|obj|node_modules|\.git)/'
# The cell must BE a pointer, not contain narrative: a pointer cell is a token, not a paragraph.
$maxCell = 60
$selfRefRx = '(?i)\b(?:this|the\s+current)\s+(?:PR|pull\s+request|commit|branch)\b'

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
            if ($cell.Length -gt $maxCell) { continue }
            if ($cell -match $selfRefRx) {
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
Write-Host "self-reference-lint: OK - $cells table cell(s) across $checked markdown file(s), none self-referential"
exit 0
