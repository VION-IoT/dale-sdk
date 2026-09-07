#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a test method that cites a spec id breaks the settled test style
  (docs/testing-conventions.md §12 names, §13 Triple-A markers).

  The rule ratchets on the citation: only a test method carrying a quoted
  "AC-…" / "SYS-…" literal in its attribute block is checked, because citing an
  id is what a suite does once it has been brought to the settled style — a
  legacy suite that cites nothing stays untouched.

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
    [string]$RepoRoot,

    # Projects a suite CITES from without owning. Overridable so the self-test can exercise the
    # mechanism when the built-in list is empty, which is the state it is designed to reach.
    [hashtable]$Exempt
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

$citationRx = '"(?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?"'
$signatureRx = '^[ \t]*public\s+(?:async\s+)?(?:Task|void)\s+(?<name>[A-Za-z0-9_]+)\s*\('

# The attribute block is SCANNED, not matched by one regex. A regex over `[...]` lines reads to the
# first `]`, which is not where a C# attribute ends in three shapes this suite uses — a `]` inside a
# string ([DataRow("Mode in ['Eco', 'Fast']", …)], [DataRow("""{ "checks": [] }""")]), a `]` closing
# a nested bracket ([DataRow(1, new[] { … })]), and an attribute wrapped onto an indented line.
# Each hid its whole method: the block never reached the signature below it, so 21 cited tests were
# checked by nothing and three of them had carried a §12 article since their area's pass with this
# gate green. A regex covering all three needs an alternation that backtracks exponentially on an
# attribute that never closes — a CI hang rather than a CI failure — so the scan below is linear
# instead and states the C# rules it knows: a string literal (raw, verbatim or quoted), a char
# literal, and bracket depth. A comment inside an attribute is a fourth rule it does not know: a `]`
# or an apostrophe inside one drops the method, as it did under the regex.

# The end of the attribute whose opening `[` is at $Start: the index just past its matching `]`,
# or -1 when the attribute never closes (which must not run away into the methods below it).
function Get-AttributeEnd([string]$Raw, [int]$Start) {
    $n = $Raw.Length
    $i = $Start
    $depth = 0
    while ($i -lt $n) {
        $c = $Raw[$i]
        if ($c -eq '"') {
            $q = 0
            while ($i + $q -lt $n -and $Raw[$i + $q] -eq '"') { $q++ }
            if ($q -ge 3) {
                # Raw string literal: closed by a run of the same length, and free to span lines.
                $delim = [string]::new('"', $q)
                $close = $Raw.IndexOf($delim, $i + $q)
                if ($close -lt 0) { return -1 }
                $i = $close + $q
                continue
            }
            $i = Get-LiteralEnd $Raw $i '"'
            if ($i -lt 0) { return -1 }
            continue
        }
        if ($c -eq "'") {
            $i = Get-LiteralEnd $Raw $i "'"
            if ($i -lt 0) { return -1 }
            continue
        }
        # Verbatim string literal, in all three spellings C# allows — @"…", $@"…", @$"…". Reading one
        # as an ordinary quoted string walks past its closing quote, because a trailing backslash
        # (@$"C:\out\") is a character there and an escape here.
        $verbatim = 0
        if ($c -eq '@' -and $i + 1 -lt $n -and $Raw[$i + 1] -eq '"') { $verbatim = 1 }
        elseif (($c -eq '@' -or $c -eq '$') -and $i + 2 -lt $n -and
                (($c -eq '@' -and $Raw[$i + 1] -eq '$') -or ($c -eq '$' -and $Raw[$i + 1] -eq '@')) -and
                $Raw[$i + 2] -eq '"') { $verbatim = 2 }
        if ($verbatim) {
            # `""` is an escaped quote; a backslash is not an escape.
            $i += $verbatim + 1
            while ($i -lt $n) {
                if ($Raw[$i] -ne '"') { $i++; continue }
                if ($i + 1 -lt $n -and $Raw[$i + 1] -eq '"') { $i += 2; continue }
                $i++
                break
            }
            continue
        }
        if ($c -eq '[') { $depth++; $i++; continue }
        if ($c -eq ']') {
            $depth--
            $i++
            if ($depth -eq 0) { return $i }
            continue
        }
        $i++
    }
    return -1
}

# The end of a quoted or char literal opening at $Start: the index just past its closing $Quote,
# or -1 if it does not close on its line — neither form spans one in C#.
function Get-LiteralEnd([string]$Raw, [int]$Start, [char]$Quote) {
    $n = $Raw.Length
    $i = $Start + 1
    while ($i -lt $n) {
        $c = $Raw[$i]
        if ($c -eq '\') { $i += 2; continue }
        if ($c -eq $Quote) { return $i + 1 }
        if ($c -eq "`n") { return -1 }
        $i++
    }
    return -1
}

# Every test method of $Raw: its attribute block, its name, the block's start and the index just
# past the signature. Blocks are leftmost and non-overlapping — once one reaches a signature, the
# scan resumes below that signature — which is what the regex this replaces produced.
function Get-TestMethodBlocks([string]$Raw) {
    $blocks = [System.Collections.Generic.List[psobject]]::new()
    $n = $Raw.Length
    $pos = 0
    while ($pos -lt $n) {
        $lineEnd = $Raw.IndexOf("`n", $pos)
        $lineEnd = if ($lineEnd -lt 0) { $n } else { $lineEnd + 1 }
        $i = $pos
        while ($i -lt $lineEnd -and ($Raw[$i] -eq ' ' -or $Raw[$i] -eq "`t")) { $i++ }
        # A block starts on a line whose first non-blank character opens an attribute.
        if ($i -ge $lineEnd -or $Raw[$i] -ne '[') { $pos = $lineEnd; continue }
        $cursor = $pos
        $landed = $false
        while ($true) {
            $j = $cursor
            while ($j -lt $n -and ($Raw[$j] -eq ' ' -or $Raw[$j] -eq "`t")) { $j++ }
            if ($j -lt $n -and $Raw[$j] -eq '[') {
                $end = Get-AttributeEnd $Raw $j
                if ($end -lt 0) { break }
                # The attribute has to end its line: code or a comment after it is not this shape.
                $k = $end
                while ($k -lt $n -and ($Raw[$k] -eq ' ' -or $Raw[$k] -eq "`t")) { $k++ }
                if ($k -lt $n -and $Raw[$k] -eq "`r") { $k++ }
                if ($k -lt $n -and $Raw[$k] -ne "`n") { break }
                $cursor = [Math]::Min($k + 1, $n)
                continue
            }
            $sigLineEnd = $Raw.IndexOf("`n", $cursor)
            if ($sigLineEnd -lt 0) { $sigLineEnd = $n }
            $sig = [regex]::Match($Raw.Substring($cursor, $sigLineEnd - $cursor), $signatureRx)
            if (-not $sig.Success) { break }
            $blocks.Add([pscustomobject]@{
                    Attrs     = $Raw.Substring($pos, $cursor - $pos)
                    Name      = $sig.Groups['name'].Value
                    Index     = $pos
                    BodyStart = $cursor + $sig.Length
                })
            $pos = $cursor + $sig.Length
            $landed = $true
            break
        }
        # A run that consumed attributes and did not land fails identically from every line inside
        # it, so resume past what it read rather than re-reading it from the next line down.
        if (-not $landed) { $pos = [Math]::Max($cursor, $lineEnd) }
    }
    return $blocks
}
# An article as a whole PascalCase word: preceded by start, a lowercase letter or a digit, and
# followed by an uppercase letter (so "Advance", "Analyzer", "Theme", "Issue" are not hits).
$articleRx = '(?:^|(?<=[a-z0-9_]))(A|An|The|Is)(?=[A-Z])'
$markerRx = '//\s*(Arrange\s*/\s*Act|Act\s*/\s*Assert|Act)\b'

$testRoots = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Directory -Filter '*.Test' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git|node_modules)[\\/]' } | ForEach-Object { $_.FullName })
if ($testRoots.Count -eq 0) {
    # `Get-ChildItem -Path @()` enumerates the process working directory, so an empty root list does
    # not scan nothing — it scans somewhere else and reports that it conformed.
    Write-Host "test-style-lint: FAIL - no *.Test directory under $RepoRoot (nothing to scan; the gate would have reported OK)."
    exit 1
}

# Projects a suite CITES from without owning — a cross-area anchor (the pilot's D3 shape:
# an analyzer registry is one area's, the ids it proves are another's). Each entry names what
# retires it; the list only shrinks. A repo-root-relative directory prefix, forward slashes.
#
# Empty since the ANLZ pass retired the last entry: every cited test in the repository is now gated.
$exempt = if ($PSBoundParameters.ContainsKey('Exempt')) { $Exempt } else { @{
} }

$problems = [System.Collections.Generic.List[string]]::new()
$checked = 0
$exempted = 0
foreach ($file in (Get-ChildItem -Path $testRoots -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
    $relPath = ($file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    if (@($exempt.Keys | Where-Object { $relPath.StartsWith($_) }).Count -gt 0) { $exempted++; continue }
    $raw = Get-Content -Raw -LiteralPath $file.FullName
    $testBlocks = @(Get-TestMethodBlocks $raw)
    if ($testBlocks.Count -eq 0) { continue }
    $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    for ($i = 0; $i -lt $testBlocks.Count; $i++) {
        $m = $testBlocks[$i]
        if ($m.Attrs -notmatch $citationRx) { continue }
        $checked++
        $name = $m.Name
        $line = ($raw.Substring(0, $m.Index) -split "`n").Count
        if ($name -cmatch $articleRx) {
            $problems.Add("${rel}:${line}: '$name' carries an article/filler word (testing-conventions section 12: drop The, A, An, Is)")
        }
        # The body runs from the signature to the next test method's attribute block, or file end.
        $bodyStart = $m.BodyStart
        $bodyEnd = if ($i + 1 -lt $testBlocks.Count) { $testBlocks[$i + 1].Index } else { $raw.Length }
        $body = $raw.Substring($bodyStart, $bodyEnd - $bodyStart)
        if ($body -notmatch $markerRx) {
            $problems.Add("${rel}:${line}: '$name' has no Triple-A markers (testing-conventions section 13: Triple-A markers)")
        }
    }
}

$staleExempt = @($exempt.Keys | Where-Object { -not (Test-Path (Join-Path $RepoRoot $_)) })
$staleExempt | ForEach-Object { Write-Host "  note: exempt entry '$_' matches no directory - remove it" }

if ($checked -eq 0 -and $exempted -eq 0) {
    # Anti-vacuous floor, the sibling of spec-trace's: this gate reported OK over 21 cited methods
    # its block pattern could not see, and zero is that failure taken to the limit. A gate that can
    # silently match nothing can silently die with nothing else in CI noticing.
    Write-Host "test-style-lint: FAIL - ZERO cited test(s) parsed across $($testRoots.Count) test root(s) (the block scan died - anti-vacuous floor)."
    exit 1
}
if ($problems.Count) {
    Write-Host "test-style-lint: FAIL - $($problems.Count) issue(s) across $checked cited test(s) ($exempted file(s) exempt):"
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "test-style-lint: OK - $checked cited test(s) conform ($exempted file(s) in exempt projects skipped)"
exit 0
