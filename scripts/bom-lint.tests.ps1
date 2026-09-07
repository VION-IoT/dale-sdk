#requires -Version 7
# Self-test for bom-lint.ps1 (no byte-order mark on the BOM-free file kinds). Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/bom-lint.tests.ps1`.
#
# Cases assert the MESSAGE and the COUNTS, not only the exit code. Both defects the gate reports -
# a byte-order mark and a NUL byte - exit 1, so an exit-code-only case cannot tell them apart; and
# no exit code at all can see a kind silently dropping out of scope, because a gate that stops
# checking .cs reports the same "OK" as one that checked them and found nothing.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'bom-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bomlint-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, [byte[]]$bytes) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    [System.IO.File]::WriteAllBytes($p, $bytes)
    return $p
}
$bom = [byte[]](0xEF, 0xBB, 0xBF)
$nulByte = [byte[]](0x00)
$text = [System.Text.Encoding]::UTF8.GetBytes("# hello`n")
$script:Output = ''
function Invoke-Lint {
    $script:Output = (pwsh -NoProfile -File $lint -RepoRoot $tmp) -join "`n"
    return $LASTEXITCODE
}
function Assert-Says($fragment, $case) {
    if ($script:Output -notmatch [regex]::Escape($fragment)) {
        throw "$case expected the report to say '$fragment'; it said:`n$script:Output"
    }
}

try {
    # A BOM-free .cs, so the C# floor fires only in case 11, which removes every .cs on purpose.
    # Without one here, each case below would land on the floor instead of on its own fixture.
    New-File 'Sdk/Core/Anchor.cs' $text | Out-Null

    # Case 1: BOM-free files of every checked kind, plus a csproj and a sln WITH a BOM. Project files
    # and the solution stay mixed by history and out of scope; C# no longer is.
    New-File 'docs/a.md' $text | Out-Null
    New-File 'web/wwwroot/app.js' $text | Out-Null
    New-File 'web/wwwroot/data.json' $text | Out-Null
    New-File '.github/workflows/ci.yml' $text | Out-Null
    New-File 'Sdk/build/Sdk.targets' $text | Out-Null
    New-File 'Sdk/Core/Thing.cs' $text | Out-Null
    New-File 'Sdk/Sdk.csproj' ($bom + $text) | Out-Null
    New-File 'Sdk/Sdk.sln' ($bom + $text) | Out-Null

    # The ci.yml above sits in a directory the OS may hide, which is why the tally below is worth
    # reading: a walk without -Force returns no hidden entry, and .github/workflows/*.yml is a kind
    # this gate covers. Unix hides it for the leading dot; Windows does not, so the attribute is set
    # here - otherwise this guard holds only on the CI runner and passes at every desk.
    if ($IsWindows) {
        $dotDir = Get-Item -LiteralPath (Join-Path $tmp '.github') -Force
        $dotDir.Attributes = $dotDir.Attributes -bor [System.IO.FileAttributes]::Hidden
    }
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (clean kinds; BOM on out-of-scope kinds) expected 0" }
    # Every number in the report is pinned, not just the total: the .cs tally is what separates a
    # gate checking C# from one that has quietly stopped, and the kind tally is what separates a scan
    # covering the whole list from one narrowed to a subset that still satisfies both floors.
    Assert-Says '7 file(s) across 6 of the 12 BOM-free kinds (2 .cs)' 'Case 1'

    # Case 2: a markdown file gains a BOM -> 1, named, and reported as a byte-order mark. The FAIL
    # header carries the same tallies as the OK line and is asserted here, or a mutation of the
    # numbers on the failing branch has no case watching it at all.
    $md = New-File 'docs/b.md' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (BOM on .md) expected 1" }
    Assert-Says 'docs/b.md: carries a UTF-8 byte-order mark' 'Case 2'
    Assert-Says '1 file(s) with a byte-order mark or a NUL byte across 8 file(s) across 6 of the 12 BOM-free kinds (2 .cs)' 'Case 2'

    # Case 2b: a second marked file. The header counts the problems it found; with only ever one
    # problem in the suite, a gate printing the constant 1 reads identically.
    $md2 = New-File 'docs/c.md' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 2b (two marked files) expected 1" }
    Assert-Says '2 file(s) with a byte-order mark or a NUL byte across 9 file(s) across 6 of the 12 BOM-free kinds (2 .cs)' 'Case 2b'
    Remove-Item $md2
    Remove-Item $md

    # Case 3: a JS file under a build output directory is skipped -> 0; the same file outside it -> 1
    New-File 'web/bin/Debug/generated.js' ($bom + $text) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 3a (bin/ skipped) expected 0" }
    $js = New-File 'web/wwwroot/bad.js' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 3b (BOM on .js) expected 1" }
    Assert-Says 'web/wwwroot/bad.js: carries a UTF-8 byte-order mark' 'Case 3b'
    Remove-Item $js

    # Case 4: a two-byte file that starts like a BOM but is shorter than one is not a BOM -> 0
    New-File 'docs/short.md' ([byte[]](0xEF, 0xBB)) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 4 (short file) expected 0" }

    # Case 4b: a file that is a BOM and nothing else -> 1. Case 4 alone pins only the short side of
    # the length test, so a gate demanding a fourth byte passes it; and a BOM-only file is exactly
    # what a helper writing utf-8-sig produces for an empty file.
    $bomOnly = New-File 'docs/bomonly.md' $bom
    if ((Invoke-Lint) -ne 1) { throw "Case 4b (file that is only a BOM) expected 1" }
    Assert-Says 'docs/bomonly.md: carries a UTF-8 byte-order mark' 'Case 4b'
    Remove-Item $bomOnly

    # Case 5: a NUL byte anywhere in a checked file -> 1. One NUL makes git call the whole file
    # binary, so grep and every reference sweep skip it - which is how a stale comment carrying
    # one survived in wwwroot/components.js. The line number is part of the report.
    $nul = New-File 'web/wwwroot/withnul.js' ($text + $nulByte + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (NUL byte in a .js) expected 1" }
    Assert-Says 'web/wwwroot/withnul.js:2: carries a NUL byte' 'Case 5'
    Remove-Item $nul

    # Case 6: the same NUL under a build output directory is skipped, like every other check -> 0
    New-File 'web/obj/Debug/generated.json' ($text + $nulByte) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 6 (NUL under obj/ skipped) expected 0" }

    # Case 7: a NUL in a .cs is now a defect, where it used to be out of scope. Asserted on the NUL
    # message, not the exit code: a .cs carrying one would also exit 1 if the gate misread it as a
    # byte-order mark.
    $csNul = New-File 'Sdk/Core/Blob.cs' ($text + $nulByte + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (NUL in a .cs) expected 1" }
    Assert-Says 'Sdk/Core/Blob.cs:2: carries a NUL byte' 'Case 7'
    Remove-Item $csNul

    # Case 8: a .cs with a byte-order mark -> 1. The whole point of the widening: this file was
    # silently in scope for nothing before T-005 normalised the 195 that carried one.
    $csBom = New-File 'Sdk/Core/Marked.cs' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 8 (BOM on a .cs) expected 1" }
    Assert-Says 'Sdk/Core/Marked.cs: carries a UTF-8 byte-order mark' 'Case 8'
    Remove-Item $csBom

    # Case 9: a NUL in a kind nobody gates is still not a defect - the rule is about the text kinds
    # this repo writes, not about every file in the tree. A .ttf carries NULs by construction.
    New-File 'web/wwwroot/Font.ttf' ($text + $nulByte) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 9 (NUL on an out-of-scope kind) expected 0" }

    # Case 10: the counts again, over the accumulated tree. Two .cs and eight checked files; the
    # skipped bin/ and obj/ files, the csproj, the sln and the ttf are in none of it. An over-count
    # is invisible to the exit code, which is why this case reads the number.
    if ((Invoke-Lint) -ne 0) { throw "Case 10 (accumulated clean tree) expected 0" }
    Assert-Says '8 file(s) across 6 of the 12 BOM-free kinds (2 .cs)' 'Case 10'

    # Case 10b: the same assertion with a different answer. Case 1 and case 10 both land on two .cs,
    # so between them they cannot tell a gate that counts C# from one printing the constant 2 - a
    # tally is only pinned by two cases that disagree about it.
    New-File 'Sdk/Core/Extra.cs' $text | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 10b (a third .cs) expected 0" }
    Assert-Says '9 file(s) across 6 of the 12 BOM-free kinds (3 .cs)' 'Case 10b'
    Remove-Item (Join-Path $tmp 'Sdk/Core/Extra.cs')

    # Case 11: the anti-vacuous floor for the widening. Files of other checked kinds remain, so the
    # scan is not empty - but no .cs reaches it, which is what a gate that has stopped checking C#
    # looks like from the outside.
    Remove-Item (Join-Path $tmp 'Sdk/Core/Anchor.cs')
    Remove-Item (Join-Path $tmp 'Sdk/Core/Thing.cs')
    if ((Invoke-Lint) -ne 1) { throw "Case 11 (no .cs reached the scan) expected 1" }
    Assert-Says 'ZERO .cs file(s)' 'Case 11'

    # Case 12: the floor for a scan that found nothing at all - a broken walk reports the same "OK"
    # as a clean tree.
    Remove-Item -Recurse -Force $tmp
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 12 (empty tree) expected 1" }
    Assert-Says 'ZERO file(s) of the BOM-free kinds' 'Case 12'

    # Case 13: the branch CI actually takes. Every case above walks the tree with Get-ChildItem,
    # because a temp fixture is not a git repo - so `git ls-files`, the only path spec-gates.yml ever
    # reaches, was covered by none of them. A repo here means tracked files are the scan's input:
    # a marked file that is tracked fails, and the same file untracked is not the gate's business.
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Host 'bom-lint.tests: SKIPPED (git not on PATH; the git-mode case cannot run)'
        exit 0
    }
    # Three kinds, not two: a scan narrowed to a pathspec that still returns C# and Markdown holds
    # both floors up, and only a fixture carrying a kind outside that pathspec notices.
    New-File 'docs/tracked.md' $text | Out-Null
    New-File 'Sdk/Core/Tracked.cs' $text | Out-Null
    New-File 'web/tracked.json' $text | Out-Null
    Push-Location $tmp
    try {
        git init --quiet 2>&1 | Out-Null
        git add -A 2>&1 | Out-Null
    }
    finally { Pop-Location }
    if ((Invoke-Lint) -ne 0) { throw "Case 13a (clean git tree) expected 0" }
    Assert-Says '3 file(s) across 3 of the 12 BOM-free kinds (1 .cs)' 'Case 13a'

    $untracked = New-File 'Sdk/Core/Untracked.cs' ($bom + $text)
    if ((Invoke-Lint) -ne 0) { throw "Case 13b (untracked marked file is not scanned) expected 0" }
    Assert-Says '3 file(s) across 3 of the 12 BOM-free kinds (1 .cs)' 'Case 13b'
    Remove-Item $untracked

    $marked = New-File 'Sdk/Core/Tracked.cs' ($bom + $text)
    Push-Location $tmp
    try { git add -A 2>&1 | Out-Null }
    finally { Pop-Location }
    if ((Invoke-Lint) -ne 1) { throw "Case 13c (tracked marked file) expected 1" }
    Assert-Says 'Sdk/Core/Tracked.cs: carries a UTF-8 byte-order mark' 'Case 13c'
    Remove-Item $marked -ErrorAction SilentlyContinue

    Write-Host 'bom-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
