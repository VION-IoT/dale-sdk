#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a text file of a kind this repo never writes with a UTF-8 byte-order mark carries
  one, OR if it carries a NUL byte: .cs .md .js .mjs .cjs .json .yml .yaml .html .css .targets
  .props. None of the repo's files of these kinds has either.

  C# joined the kinds once its files were normalised in one commit. Before that they were mixed,
  and the mixing is what hid the defect: the compiler does not care about a mark, so a scan of C#
  told nobody anything, and a mark arriving on one file was indistinguishable from the files that
  already carried one. cleanupcode was probed first and rewrites a BOM-less file without adding one
  back, so the style gate and this one do not fight.

  Project files (.csproj), the solution, .DotSettings and the .scriban generator template stay out
  of scope and stay mixed. Visual Studio and ReSharper rewrite the first three on their own terms,
  so gating them would fail an author for an IDE this repo supports; the template is read through a
  StreamReader that strips a mark before it can reach generated code.

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

$kinds = @('.cs', '.md', '.js', '.mjs', '.cjs', '.json', '.yml', '.yaml', '.html', '.css', '.targets', '.props')

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
    # -Force, or the walk is quietly OS-dependent: on Unix a dot-prefixed entry is hidden, and
    # Get-ChildItem omits hidden entries without it - so .github/workflows/*.yml, a kind this gate
    # covers, was scanned on Windows and skipped on Linux. The exclusions below still drop .git/.
    $files = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } | ForEach-Object { $_.FullName })
}

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
$csChecked = 0
$kindsSeen = [System.Collections.Generic.HashSet[string]]::new()
foreach ($path in $files) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($kinds -notcontains $ext) { continue }
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $checked++
    if ($ext -eq '.cs') { $csChecked++ }
    $kindsSeen.Add($ext) | Out-Null
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

# Anti-vacuous floors, the siblings of pragma-reason-lint's: a scan that reaches nothing reports that
# everything conformed, which is the same output as a scan that is silently broken. The second floor
# is the one C# needs - dropping '.cs' from $kinds leaves every other kind still checked, so the
# total stays healthy while the largest kind in the tree goes unread.
#
# Both tallies below are printed, not just floored. A floor only catches a count falling to zero, and
# the way a scan narrows in practice is partial: a pathspec that still returns C# but no longer
# returns .json holds both floors up while the gate quietly stops covering four kinds. The numbers in
# the report are what a self-test can pin, and what a reader compares against the run before.
if ($checked -eq 0) {
    Write-Host "bom-lint: FAIL - ZERO file(s) of the BOM-free kinds reached the scan (nothing was judged - anti-vacuous floor)."
    exit 1
}
if ($csChecked -eq 0) {
    Write-Host "bom-lint: FAIL - ZERO .cs file(s) reached the scan across $checked checked (C# coverage is inert - anti-vacuous floor)."
    exit 1
}

$tally = "$checked file(s) across $($kindsSeen.Count) of the $($kinds.Count) BOM-free kinds ($csChecked .cs)"
if ($problems.Count) {
    Write-Host "bom-lint: FAIL - $($problems.Count) file(s) with a byte-order mark or a NUL byte across $tally`:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "bom-lint: OK - $tally, none carries a byte-order mark or a NUL byte"
exit 0
