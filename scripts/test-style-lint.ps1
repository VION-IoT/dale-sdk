#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a test method that cites a spec id breaks the settled test style
  (docs/testing-conventions.md §12 names, §13 Triple-A markers).

  The rule ratchets on the citation: only a test method carrying a quoted
  "AC-…" / "SYS-…" literal in its attribute block is checked, because citing an
  id is what an area pass does when it brings a suite to the settled style —
  legacy suites that cite nothing are untouched until their pass.

  Checks per cited test method:
    - the method name carries no article or filler token — A, An, The, Is —
      as a PascalCase word (§12: "drop The, A, It, Is")
    - the method body carries Triple-A markers in some accepted form: a
      `// Act` marker, or the combined `// Arrange / Act` / `// Act / Assert`
      (§13: "Every test carries // Arrange, // Act, // Assert — always")

  Scans every *.Test directory in the repo (the xunit projects live nested under
  examples/, libraries/, templates/), skipping bin/ and obj/.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'test-style-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
# Long-name, canonical form: a caller may pass an 8.3 (JONASB~1) or forward-slash spelling, and
# every relative path below is computed by prefix length against the long names Get-ChildItem
# returns. GetFullPath expands the short segments; Resolve-Path does not.
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

# A test method: its attribute block (one or more [...] lines) followed by the signature. The
# attribute block is captured so the citation check sees [TestProperty("spec", "AC-…")] /
# [Trait("spec", "AC-…")] and nothing else.
$testRx = '(?ms)(?<attrs>(?:^[ \t]*\[[^\]\r\n]*\][ \t]*\r?\n)+)[ \t]*public\s+(?:async\s+)?(?:Task|void)\s+(?<name>[A-Za-z0-9_]+)\s*\('
$citationRx = '"(?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?"'
# An article as a whole PascalCase word: preceded by start, a lowercase letter or a digit, and
# followed by an uppercase letter (so "Advance", "Analyzer", "Theme", "Issue" are not hits).
$articleRx = '(?:^|(?<=[a-z0-9_]))(A|An|The|Is)(?=[A-Z])'
$markerRx = '//\s*(Arrange\s*/\s*Act|Act\s*/\s*Assert|Act)\b'

$testRoots = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Directory -Filter '*.Test' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git|node_modules)[\\/]' } | ForEach-Object { $_.FullName })

# Projects an area pass CITES from without owning — a cross-area anchor (the pilot's D3 shape:
# an analyzer registry is one area's, the ids it proves are another's). Each entry names the pass
# that retires it; the list only shrinks. A repo-root-relative directory prefix, forward slashes.
$exempt = @{
}

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
$exempted = 0
foreach ($file in (Get-ChildItem -Path $testRoots -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
    $relPath = ($file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    if (@($exempt.Keys | Where-Object { $relPath.StartsWith($_) }).Count -gt 0) { $exempted++; continue }
    $raw = Get-Content -Raw -LiteralPath $file.FullName
    $testMatches = [regex]::Matches($raw, $testRx)
    if ($testMatches.Count -eq 0) { continue }
    $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    for ($i = 0; $i -lt $testMatches.Count; $i++) {
        $m = $testMatches[$i]
        if ($m.Groups['attrs'].Value -notmatch $citationRx) { continue }
        $checked++
        $name = $m.Groups['name'].Value
        $line = ($raw.Substring(0, $m.Index) -split "`n").Count
        if ($name -cmatch $articleRx) {
            $problems.Add("${rel}:${line}: '$name' carries an article/filler word (testing-conventions section 12: drop The, A, An, Is)")
        }
        # The body runs from the signature to the next test method's attribute block, or file end.
        $bodyStart = $m.Index + $m.Length
        $bodyEnd = if ($i + 1 -lt $testMatches.Count) { $testMatches[$i + 1].Index } else { $raw.Length }
        $body = $raw.Substring($bodyStart, $bodyEnd - $bodyStart)
        if ($body -notmatch $markerRx) {
            $problems.Add("${rel}:${line}: '$name' has no Triple-A markers (testing-conventions section 13: Triple-A markers)")
        }
    }
}

$staleExempt = @($exempt.Keys | Where-Object { -not (Test-Path (Join-Path $RepoRoot $_)) })
$staleExempt | ForEach-Object { Write-Host "  note: exempt entry '$_' matches no directory - remove it" }

if ($problems.Count) {
    Write-Host "test-style-lint: FAIL - $($problems.Count) issue(s) across $checked cited test(s) ($exempted file(s) exempt):"
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "test-style-lint: OK - $checked cited test(s) conform ($exempted file(s) in exempt projects skipped)"
exit 0
