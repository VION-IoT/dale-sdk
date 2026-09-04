#requires -Version 7
# Self-test for bom-lint.ps1 (no byte-order mark on the BOM-free file kinds). Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/bom-lint.tests.ps1`.
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
$text = [System.Text.Encoding]::UTF8.GetBytes("# hello`n")
function Invoke-Lint {
    pwsh -NoProfile -File $lint -RepoRoot $tmp | Out-Null
    return $LASTEXITCODE
}

try {
    # Case 1: BOM-free files of every checked kind, plus a C# file and a csproj WITH a BOM (out of scope) -> 0
    New-File 'docs/a.md' $text | Out-Null
    New-File 'web/wwwroot/app.js' $text | Out-Null
    New-File 'web/wwwroot/data.json' $text | Out-Null
    New-File '.github/workflows/ci.yml' $text | Out-Null
    New-File 'Sdk/build/Sdk.targets' $text | Out-Null
    New-File 'Sdk/Core/Thing.cs' ($bom + $text) | Out-Null
    New-File 'Sdk/Sdk.csproj' ($bom + $text) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (clean kinds; BOM on out-of-scope kinds) expected 0" }

    # Case 2: a markdown file gains a BOM -> 1
    $md = New-File 'docs/b.md' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (BOM on .md) expected 1" }
    Remove-Item $md

    # Case 3: a JS file under a build output directory is skipped -> 0; the same file outside it -> 1
    New-File 'web/bin/Debug/generated.js' ($bom + $text) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 3a (bin/ skipped) expected 0" }
    $js = New-File 'web/wwwroot/bad.js' ($bom + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 3b (BOM on .js) expected 1" }
    Remove-Item $js

    # Case 4: a two-byte file that starts like a BOM but is shorter than one is not a BOM -> 0
    New-File 'docs/short.md' ([byte[]](0xEF, 0xBB)) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 4 (short file) expected 0" }

    # Case 5: a NUL byte anywhere in a checked file -> 1. One NUL makes git call the whole file
    # binary, so grep and every reference sweep skip it - which is how a stale comment carrying
    # one survived in wwwroot/components.js.
    $nul = New-File 'web/wwwroot/withnul.js' ($text + [byte[]](0x00) + $text)
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (NUL byte in a .js) expected 1" }
    Remove-Item $nul

    # Case 6: the same NUL under a build output directory is skipped, like every other check -> 0
    New-File 'web/obj/Debug/generated.json' ($text + [byte[]](0x00)) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 6 (NUL under obj/ skipped) expected 0" }

    # Case 7: a file of an unchecked kind may carry a NUL - the rule is about the text kinds this
    # repo writes, not about every file in the tree -> 0
    New-File 'Sdk/Core/Blob.cs' ($text + [byte[]](0x00)) | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 7 (NUL on an out-of-scope kind) expected 0" }

    Write-Host 'bom-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
