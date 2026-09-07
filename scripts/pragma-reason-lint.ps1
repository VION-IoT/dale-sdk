#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if a `#pragma warning disable` covering a DALE diagnostic carries no reason, or a reason
  built from nothing but the directive's own words.

  A suppression is a claim that the shape below it is intended - the one kind of claim that ages
  worst, because the code it exempts keeps compiling clean after the intent is gone. Nothing else in
  the toolchain asks for that claim to be written down: the compiler accepts a bare directive,
  cleanupcode reformats it, and every test passes. The rule this gates is
  docs/testing-conventions.md section 8.

  Where the reason may be:
    - after the directive on the same line (`#pragma warning disable DALE045 // ...`), or
    - in the run of `//` comment lines directly above it, or a `/* */` block ending on the line
      above (docs/comment-conventions.md section Form names both forms).

  Other `#pragma warning` lines between the comment and the directive are read through, so one
  reason above a stack of directives serves all of them.

  A `///` run above counts only when it NAMES one of the ids being disabled. A doc comment documents
  the member below it, not the suppression, so accepting one unconditionally would let every
  documented member satisfy the gate with prose that never mentions the pragma; naming the id is the
  author connecting the two on purpose.

  What is not a reason: a comment left with nothing to say once the directive's own words are struck.
  `// Suppress DALE045 here.` repeats the line it sits on. The check strips every `DALE####` token,
  XML doc tags, words of three letters or fewer, and the directive's own vocabulary, then asks that
  ONE word remain. One, not a prose-quality floor: a higher bar rejects `// Bug in the tool; see
  VION-133.`, which is the one citation form docs/comment-conventions.md blesses, and still admits
  any non-reason padded out with adverbs. What a lint can judge is whether the comment says
  something the directive does not, which is the whole of the rule.

  Scope is a directive that suppresses a DALE diagnostic: one naming a `DALE####` id, or a bare
  `#pragma warning disable` with no id at all, which suppresses every diagnostic including these.
  Ids are read from the directive itself, never from its trailing comment, so a `CS8618` suppression
  whose reason mentions a DALE id is not a site.

  Two shapes it reads wrong, both benign: a directive inside an embedded-source string (an analyzer
  fixture holding C# as text) is treated as real, and the fix is to write the reason into the
  embedded source, which is C# too; a directive inside a `/* */` block is likewise read as code.
  Neither shape exists in the tree today.

  Scans every *.cs under the repo root, skipping bin/, obj/, node_modules/ and .git/.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'pragma-reason-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
# Long-name, canonical form (an 8.3 or forward-slash spelling breaks the prefix arithmetic below).
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

# A directive is the first non-blank thing on its line - C# allows nothing else before it - so a
# `#pragma` quoted inside a comment (DaleDiagnostics.cs documents the escape hatch that way) or
# assigned to a string is not a site.
$disableRx = '^[ \t]*#pragma\s+warning\s+disable\b'
$anyPragmaRx = '^[ \t]*#pragma\s+warning\b'
$daleIdRx = 'DALE\d+'

# The directive's own vocabulary: words a comment can be built from without saying anything the
# pragma line does not already say. Struck, with every word of three letters or fewer, before the
# one-word floor is applied.
$directiveWords = @(
    'pragma', 'warning', 'warnings', 'disable', 'disabled', 'disables',
    'restore', 'restored', 'restores', 'suppress', 'suppressed', 'suppresses', 'suppression',
    'analyzer', 'analyzers', 'diagnostic', 'diagnostics', 'rule', 'rules', 'here', 'this'
)

# The substantive words of $Text: no DALE id, no XML doc tag, nothing under four letters, nothing the
# directive already says. One of them is the floor a reason has to clear.
function Get-SubstantiveWords([string]$Text) {
    $stripped = [regex]::Replace($Text, '<[^>]*>', ' ')
    $stripped = [regex]::Replace($stripped, $daleIdRx, ' ')
    return @([regex]::Matches($stripped, "[A-Za-z][A-Za-z']*") |
        ForEach-Object { $_.Value.ToLowerInvariant() } |
        Where-Object { $_.Length -ge 4 -and $directiveWords -notcontains $_ })
}

$problems = [System.Collections.Generic.List[string]]::new()
$files = 0
$sites = 0
foreach ($file in (Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' })) {
    $files++
    $rel = ($file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($n = 0; $n -lt $lines.Count; $n++) {
        $line = $lines[$n]
        if ($line -notmatch $disableRx) { continue }

        # The directive is what the ids are read from; its trailing comment is prose that may name
        # any id at all.
        $split = $line.IndexOf('//')
        $directive = if ($split -ge 0) { $line.Substring(0, $split) } else { $line }
        $ids = @([regex]::Matches($directive, $daleIdRx) | ForEach-Object { $_.Value })
        $blanket = $directive.Trim() -match '^#pragma\s+warning\s+disable\s*$'
        if ($ids.Count -eq 0 -and -not $blanket) { continue }
        $sites++
        $lineNo = $n + 1
        $idList = if ($ids.Count) { $ids -join ', ' } else { 'every diagnostic' }

        # The trailing comment, then the comment carrier directly above: a blank line or any code
        # between ends the run, because a reason two members up is not this directive's. Another
        # `#pragma warning` line is read through - a stack of directives shares one reason.
        $reason = if ($split -ge 0) { $line.Substring($split + 2) } else { '' }
        $found = $split -ge 0
        for ($m = $n - 1; $m -ge 0; $m--) {
            $above = $lines[$m].TrimStart()
            if ($above -match $anyPragmaRx) { continue }
            if ($above.StartsWith('///')) {
                # A doc comment run: it belongs to the member below, and counts only where it names
                # what is being disabled.
                $docText = ''
                for ($d = $m; $d -ge 0 -and $lines[$d].TrimStart().StartsWith('///'); $d--) {
                    $docText = $lines[$d].TrimStart().Substring(3) + ' ' + $docText
                }
                if (@($ids | Where-Object { $docText -match $_ }).Count -gt 0) {
                    $found = $true
                    $reason = "$reason $docText"
                }
                break
            }
            if ($above.StartsWith('//')) {
                $found = $true
                $reason = $above.Substring(2) + ' ' + $reason
                continue
            }
            if ($above.EndsWith('*/')) {
                # A block comment: read up to the line that opens it, which may be this one.
                for ($d = $m; $d -ge 0; $d--) {
                    $reason = $lines[$d] + ' ' + $reason
                    if ($lines[$d].Contains('/*')) { $found = $true; break }
                }
            }
            break
        }

        if (-not $found) {
            $problems.Add("${rel}:${lineNo}: '$idList' is suppressed with no reason - say why after the directive or directly above it (a suppression is a claim that the shape below is intended)")
            continue
        }
        if ((Get-SubstantiveWords $reason).Count -eq 0) {
            $problems.Add("${rel}:${lineNo}: '$idList' is suppressed with a comment built only from the directive's own words - say something the directive does not already say")
        }
    }
}

if ($sites -eq 0) {
    # Anti-vacuous floor, the sibling of test-style-lint's: a scan that matches nothing reports that
    # everything conformed, which is the same output as a scan that is silently broken. This repo has
    # DALE suppressions; if the last one is ever legitimately gone, this gate goes with it rather than
    # standing green over nothing.
    Write-Host "pragma-reason-lint: FAIL - ZERO DALE suppression site(s) parsed across $files file(s) (nothing was judged - anti-vacuous floor)."
    exit 1
}
if ($problems.Count) {
    Write-Host "pragma-reason-lint: FAIL - $($problems.Count) of $sites DALE suppression(s) across $files file(s) do not say why:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "pragma-reason-lint: OK - $sites DALE suppression(s) in $files file(s), each with a reason"
exit 0
