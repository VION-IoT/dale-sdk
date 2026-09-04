#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a text file of a kind this repo never writes with a UTF-8 byte-order mark carries
  one, OR if it carries a NUL byte: .md .js .mjs .cjs .json .yml .yaml .html .css .targets
  .props. None of the repo's files of these kinds has either; C# files, project files and the
  solution are mixed by history for the BOM and are left alone (the compiler does not care).

  The BOM shape it catches: a helper writing `utf-8-sig`, which strips a BOM on read and writes
  one on every write - one pass prepended a BOM to 46 files, invisible to every gate, showing as
  a spurious first-line change in each diff and two files differing from main by the BOM alone.

  The NUL shape it catches: one raw NUL anywhere in a text file makes git call the whole file
  BINARY (`git ls-files --eol` shows `i/-text`), so the repo's line-ending policy never
  normalises it and every `grep` skips it with one "binary file matches" line. A file no grep
  reads is a file no reference sweep, no style gate and no review reaches - components.js
  carried one inside a comment for months, with 22 RFC citations inside it that no sweep saw.

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
    $rel = ($path.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $problems.Add("${rel}: carries a UTF-8 byte-order mark; files of this kind never do here (a helper writing utf-8-sig?)")
    }
    $nul = [Array]::IndexOf($bytes, [byte]0)
    if ($nul -ge 0) {
        # Report the line so the fix is one jump away: one NUL makes git treat the WHOLE file as
        # binary, so grep, the line-ending policy and every sweep stop seeing it.
        $line = 1
        for ($i = 0; $i -lt $nul; $i++) { if ($bytes[$i] -eq 10) { $line++ } }
        $problems.Add("${rel}:${line}: carries a NUL byte; git then calls the file binary and every grep and sweep skips it")
    }
}

if ($problems.Count) {
    Write-Host "bom-lint: FAIL - $($problems.Count) file(s) with a byte-order mark or a NUL byte across $checked checked:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "bom-lint: OK - $checked file(s) of the BOM-free kinds, none carries a byte-order mark or a NUL byte"
exit 0
