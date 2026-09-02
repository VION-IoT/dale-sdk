#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a C# doc-comment block carries more than one <summary> element.

  The shape it catches: an edit anchored on a declaration inserts a new member ABOVE the
  anchor and BELOW the existing doc comment. The new member takes that comment on top of
  its own - two <summary> blocks on one declaration - and the old member is left with none.
  The compiler accepts it, cleanupcode reformats it, every test passes; only a reader
  notices, and one pass drew the same review finding twice in a day. The lint sees the
  double; the bare half needs the insertion point re-read (docs/sdk-surface-conventions.md
  section 2).

  A block is a run of `///` lines. A blank line does not end it, because the compiler
  attaches every doc-comment run preceding a declaration to that declaration; any other
  line does. Scans every *.cs under the repo root, skipping bin/, obj/, node_modules/ and
  .git/.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'doc-comment-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
# Long-name, canonical form (an 8.3 or forward-slash spelling breaks the prefix arithmetic below).
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

$summaryRx = '<summary[\s/>]'
$problems = [System.Collections.Generic.List[string]]::new()
$files = 0
$blocks = 0
foreach ($file in (Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' })) {
    $files++
    $rel = ($file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $inBlock = $false
    $start = 0
    $summaries = 0
    for ($n = 0; $n -le $lines.Count; $n++) {
        # One virtual line past the end closes a block the file ends on.
        $line = if ($n -lt $lines.Count) { $lines[$n] } else { 'EOF' }
        if ($line -match '^\s*///') {
            if (-not $inBlock) { $inBlock = $true; $start = $n + 1; $summaries = 0 }
            if ($line -match $summaryRx) { $summaries++ }
            continue
        }
        if ($line -match '^\s*$') { continue }
        if ($inBlock) {
            $blocks++
            if ($summaries -gt 1) {
                $problems.Add("${rel}:${start}: doc block carries $summaries <summary> elements - one declaration took two doc comments (an insertion anchored below a doc comment steals it; the member above the anchor is now bare)")
            }
            $inBlock = $false
        }
    }
}

if ($problems.Count) {
    Write-Host "doc-comment-lint: FAIL - $($problems.Count) doc block(s) with more than one <summary> across $files file(s):"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "doc-comment-lint: OK - $blocks doc block(s) in $files file(s), none carries a second <summary>"
exit 0
