#requires -Version 7
# Self-test for sweep-residue-lint.ps1 (no sweep residue in prose). Plain pwsh, NOT Pester.
# Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/sweep-residue-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'sweep-residue-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("residuelint-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, [string[]]$lines) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    [System.IO.File]::WriteAllLines($p, $lines, [System.Text.UTF8Encoding]::new($false))
    return $p
}
function Invoke-Lint {
    $script:out = & pwsh -NoProfile -File $lint -RepoRoot $tmp 2>&1 | Out-String
    return $LASTEXITCODE
}
function Expect([int]$code, [string]$case, [string]$mentions) {
    $rc = Invoke-Lint
    if ($rc -ne $code) { throw "$case expected exit $code, got $rc`n$script:out" }
    if ($mentions -and ($script:out -notmatch [regex]::Escape($mentions))) { throw "$case expected the output to mention '$mentions'`n$script:out" }
}

try {
    # Case 1: every legitimate look-alike of a residue, in every scanned kind -> 0
    New-File 'docs/clean.md' @(
        '# A heading with () and  two spaces is not prose',
        '',
        'A sentence naming `Foo()` and a [link](https://example.org/a()b) and a URL https://x.y/() in prose.',
        'A table follows.',
        '',
        '| col  a | ()  |',
        '|---|---|',
        '| x  y | z |',
        '',
        '```',
        'code ( ) with  double  spaces',
        '```',
        '',
        '    indented code ( )  here',
        '',
        'A hard line break has two trailing spaces  ',
        'and continues here. An entity &lt;T&gt;() is masked. <!-- a comment ( ) -->',
        '',
        '<!--',
        'a multi-line comment ( ) with  residue shapes',
        '-->',
        '- a list item (with a parenthetical) and',
        '  its continuation line (wrapped) reads fine.'
    ) | Out-Null
    New-File 'Sdk/Thing.cs' @(
        '/// <summary>Registered via <c>WithDi()</c>; see <see cref="Foo()" /> and Stopping() and Build&lt;T&gt;().</summary>',
        '// ---------------------------------------------',
        '// @formatter:off  aligned   table  here',
        'var x = Call( );  // code, not prose',
        'int a = 1;  int b = 2;  var c = Call (); // two spaces and an empty pair in CODE are not judged',
        '//   Case 1:   an aligned comment column uses three spaces',
        'public void M() { }'
    ) | Out-Null
    New-File 'web/wwwroot/app.js' @(
        '// A picker over contractRefs(); the value is the pair itself.',
        'const a = f( );'
    ) | Out-Null
    Expect 0 'Case 1 (legitimate look-alikes)' '3 file(s)'

    # Case 2: an empty pair on its own in Markdown prose -> 1
    $f = New-File 'docs/bad.md' @('The rule is stated in the spec () and applied at parse time.')
    Expect 1 'Case 2 (empty pair in prose)' 'empty parenthesis pair'
    Remove-Item $f

    # Case 3: two spaces inside a sentence in a C# comment -> 1
    $f = New-File 'Sdk/Bad.cs' @('// resolves the path off segment two  or three, so four resolves.')
    Expect 1 'Case 3 (double space in a comment)' 'two spaces inside a sentence'
    Remove-Item $f

    # Case 4: a Markdown line ending on an opening parenthesis -> 1; the same shape in a C# doc comment is a wrap, not a residue -> 0
    $f = New-File 'docs/bad2.md' @('The four definition sites agree (', 'as the runner already checks).')
    Expect 1 'Case 4a (open paren at end of a Markdown line)' 'ends on an opening parenthesis'
    Remove-Item $f
    $f = New-File 'Sdk/Wrap.cs' @('///     so CI can run just this fast (', '///     filter).')
    Expect 0 'Case 4b (open paren at end of a doc-comment line)' ''
    Remove-Item $f

    # Case 5: residue in the kinds and places outside the scope -> 0
    New-File 'docs/rfcs/0001-old.md' @('Frozen history () with  residue.') | Out-Null
    New-File 'docs/changes/archive/2026-01-01-x.md' @('Archived () with  residue.') | Out-Null
    New-File 'docs/process-journal.md' @('2026-01-01 · gate · x · Journal () with  residue.') | Out-Null
    New-File 'web/wwwroot/vendor.min.js' @('// minified ( ) vendored  text') | Out-Null
    New-File 'web/bin/Debug/gen.md' @('Build output () with  residue.') | Out-Null
    New-File 'web/Notes.txt' @('Not a scanned kind () with  residue.') | Out-Null
    Expect 0 'Case 5 (out of scope)' ''

    Write-Host 'sweep-residue-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
