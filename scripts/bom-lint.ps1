#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a text file of a kind this repo never writes with a UTF-8 byte-order mark carries
  one: .md .js .mjs .cjs .json .yml .yaml .html .css .targets .props. None of the repo's files
  of these kinds has a BOM; C# files, project files and the solution are mixed by history and
  are left alone (the compiler does not care).

  The shape it catches: a helper writing `utf-8-sig`, which strips a BOM on read and writes one
  on every write - one pass prepended a BOM to 46 files, invisible to every gate, showing as a
  spurious first-line change in each diff and two files differing from main by the BOM alone.

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
    if (-not $RepoRoot) { Write-Host 'bom-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

$kinds = @('.md', '.js', '.mjs', '.cjs', '.json', '.yml', '.yaml', '.html', '.css', '.targets', '.props')

$files = @()
$inGit = $false
Push-Location $RepoRoot
try {
    $top = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top -and ([System.IO.Path]::GetFullPath($top.Trim()).TrimEnd('\', '/') -eq $RepoRoot.TrimEnd('\', '/'))) {
        $inGit = $true
        $files = @(git ls-files -z | ForEach-Object { $_ } ) -split "`0" | Where-Object { $_ } | ForEach-Object { Join-Path $RepoRoot $_ }
    }
}
finally { Pop-Location }
if (-not $inGit) {
    $files = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } | ForEach-Object { $_.FullName })
}

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
foreach ($path in $files) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($kinds -notcontains $ext) { continue }
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $checked++
    $head = [byte[]]::new(3)
    $stream = [System.IO.File]::OpenRead($path)
    try { $n = $stream.Read($head, 0, 3) } finally { $stream.Dispose() }
    if ($n -eq 3 -and $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF) {
        $rel = ($path.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
        $problems.Add("${rel}: carries a UTF-8 byte-order mark; files of this kind never do here (a helper writing utf-8-sig?)")
    }
}

if ($problems.Count) {
    Write-Host "bom-lint: FAIL - $($problems.Count) file(s) with a byte-order mark across $checked checked:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "bom-lint: OK - $checked file(s) of the BOM-free kinds, none carries one"
exit 0
