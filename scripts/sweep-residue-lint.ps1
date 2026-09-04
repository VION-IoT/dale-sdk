#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if prose carries the residue a scripted reference sweep leaves behind: an empty
  parenthesis pair standing on its own (` ()` - the citation it held is gone), two spaces
  inside a sentence (`was  the` - a token deleted from its middle), or, in Markdown, a line
  that ends on an opening parenthesis (the parenthetical's content was the deleted span).

  Prose is: every Markdown line outside code fences, tables, headings, HTML comments and
  indented code, with inline code spans, tags, entities, links and URLs masked; and the text
  of `//` / `///` comment lines in C# and JavaScript, likewise masked. Code itself is never
  judged, and `Stopping()` in a comment is a method name, not a residue - an empty pair counts
  only when nothing identifier-like precedes it.

  The shape it catches: one pass absorbed six RFCs with a scripted sweep and left five residue
  sentences behind - in a convention doc and a resolver's comments - each read as prose by the
  next reader and seen by no gate. Frozen RFCs, the append-only logs (the journal, the retro
  notes, archived change docs), the generated snapshots and vendored minified scripts are
  outside its scope.

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
    if (-not $RepoRoot) { Write-Host 'sweep-residue-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

$kinds = @('.md', '.cs', '.js', '.mjs')
# Outside the scope, by repo-root-relative path (forward slashes): history and generated or vendored text.
$outOfScopeRx = '^docs/(rfcs|retro|changes/archive|snapshots)/|^docs/process-journal\.md$|\.min\.js$|vue\.esm-browser\.prod\.js$|(^|/)(bin|obj|node_modules|\.git)/'

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

# Masks: what is not prose inside a prose line. Order matters - a link's URL is masked with the link.
function Mask-Prose([string]$s) {
    $s = [regex]::Replace($s, '`[^`]*`', 'CODE')
    $s = [regex]::Replace($s, '<c>[^<]*</c>', 'CODE')
    $s = [regex]::Replace($s, '<code>[^<]*</code>', 'CODE')
    $s = [regex]::Replace($s, '<[^>]+>', 'TAG')
    $s = [regex]::Replace($s, '&[a-z]+;', 'ENT')
    $s = [regex]::Replace($s, '\[[^\]]*\]\([^)]*\)', 'LINK')
    $s = [regex]::Replace($s, 'https?://\S+', 'URL')
    return $s
}
$emptyPairRx = '(?<![\w>;])\(\s*\)'
$doubleSpaceRx = '(?<=[A-Za-z0-9,.;:)])  (?=[A-Za-z0-9(])'
$openParenEolRx = '\(\s*$'

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
foreach ($path in $files) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($kinds -notcontains $ext) { continue }
    $rel = ($path.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    if ($rel -match $outOfScopeRx) { continue }
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $checked++
    $lines = [System.IO.File]::ReadAllLines($path)
    $isMd = $ext -eq '.md'
    $inFence = $false
    $inComment = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($isMd) {
            if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
            if ($inFence) { continue }
            if ($inComment) { if ($line -match '-->') { $inComment = $false }; continue }
            if ($line -match '^\s*<!--') { if ($line -notmatch '-->') { $inComment = $true }; continue }
            if ($line -match '^\s*(\||#)') { continue }
            if ($line -match '^\s{4,}\S' -and $line -notmatch '^\s*([-*+]|\d+\.)\s') { continue }
            $text = Mask-Prose $line
        }
        else {
            $m = [regex]::Match($line, '^\s*//+\s?(?<t>.*)$')
            if (-not $m.Success) { continue }
            $text = $m.Groups['t'].Value
            if ($text -match '^\s*(@formatter|[-=*]{3,})') { continue }
            $text = Mask-Prose $text
        }
        $n = $i + 1
        if ($text -match $emptyPairRx) { $problems.Add("${rel}:${n}: an empty parenthesis pair on its own - the citation it held is gone: $($line.Trim())") }
        if ($text -match $doubleSpaceRx) { $problems.Add("${rel}:${n}: two spaces inside a sentence - a token was deleted from its middle: $($line.Trim())") }
        if ($isMd -and $text -match $openParenEolRx) { $problems.Add("${rel}:${n}: the line ends on an opening parenthesis - its content was the deleted span: $($line.Trim())") }
    }
}

if ($problems.Count) {
    Write-Host "sweep-residue-lint: FAIL - $($problems.Count) residue(s) across $checked file(s):"
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "sweep-residue-lint: OK - $checked file(s) of prose-bearing kinds, no sweep residue"
exit 0
