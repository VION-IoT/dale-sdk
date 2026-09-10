#requires -Version 7
# Self-test for packed-msbuild-lint.ps1 (every MSBuild file this repo packs under a NuGet build
# folder parses). Plain pwsh, NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`;
# just this one: `pwsh -File scripts/packed-msbuild-lint.tests.ps1`.
#
# Case 2 carries the founding fixture: a comment beginning on the literal `--package-id`, the
# text that shipped as 0.12.0 and failed every consumer's build with MSB4024. The gate is not
# trusted until that fixture reddens, so it is asserted on the parser's own message and on the
# line and column, not on the exit code - a gate that reddened for any other reason would pass
# an exit-code-only case while catching nothing.
#
# Cases assert the MESSAGE and the COUNTS. Every failure this gate reports exits 1, so an exit
# code cannot tell a malformed targets file from a missing one or from a floor; and no exit code
# at all can see the derivation quietly narrowing, because a gate that has stopped finding the
# packed entry reports the same "OK" as one that found it and parsed it.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'packed-msbuild-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("packedmsbuild-" + [guid]::NewGuid().ToString('N'))

function Reset-Tree {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
}
function New-TextFile($rel, $text) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    [System.IO.File]::WriteAllText($p, $text)
    return $p
}
function New-Project($rel, [string[]]$items) {
    $body = ($items | ForEach-Object { "        $_" }) -join "`n"
    return New-TextFile $rel "<Project Sdk=`"Microsoft.NET.Sdk`">`n    <ItemGroup>`n$body`n    </ItemGroup>`n</Project>`n"
}

# A well-formed targets file, and the two ways one stops being well-formed. The first is the
# founding shape; the second is any other malformedness, which is what separates an XML parse
# from a grep for a double hyphen.
$goodTargets = "<Project>`n    <Target Name=`"Run`"/>`n</Project>`n"
$dashTargets = "<Project>`n    <!-- --package-id passes this project's PackageId -->`n    <Target Name=`"Run`"/>`n</Project>`n"
$unclosedTargets = "<Project>`n    <Target Name=`"Run`">`n</Project>`n"

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
function Assert-Silent($fragment, $case) {
    if ($script:Output -match [regex]::Escape($fragment)) {
        throw "$case expected the report NOT to say '$fragment'; it said:`n$script:Output"
    }
}

try {
    # Case 1: one project packing one well-formed targets file under build/ -> 0. The tally is
    # pinned here and disagreed with in case 1b, because a single case cannot tell a counter from
    # a constant.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (one clean packed targets file) expected 0" }
    Assert-Says '1 packed MSBuild file(s) from 1 entr(ies) across 1 project file(s)' 'Case 1'

    # Case 1b: a second project packing a second file. Every number in the tally moves.
    New-TextFile 'Io/buildTransitive/Io.props' $goodTargets | Out-Null
    New-Project 'Io/Io.csproj' @('<None Include="buildTransitive/Io.props" Pack="true" PackagePath="buildTransitive/"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 1b (a second project, buildTransitive) expected 0" }
    Assert-Says '2 packed MSBuild file(s) from 2 entr(ies) across 2 project file(s)' 'Case 1b'

    # Case 2: THE founding fixture. A comment that begins on the literal switch name `--package-id`
    # reads as ordinary prose and is not well-formed XML. This is the text 0.12.0 shipped.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $dashTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (a comment containing '--') expected 1" }
    # The parser's own words, which are the words MSB4024 quotes back to the consumer - and the
    # line, which is where the author goes.
    Assert-Says "Sdk/build/Sdk.targets:2:" 'Case 2'
    Assert-Says "An XML comment cannot contain '--'" 'Case 2'
    Assert-Says 'MSB4024' 'Case 2'
    Assert-Says '1 packed MSBuild file(s) MSBuild cannot import' 'Case 2'

    # Case 3: malformed in a way that has no double hyphen in it at all -> 1. Case 2 alone is
    # satisfied by a grep for `--`, which would pass every other file MSBuild cannot import.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $unclosedTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 3 (an unclosed element) expected 1" }
    Assert-Silent "An XML comment cannot contain" 'Case 3'
    Assert-Says 'Sdk/build/Sdk.targets:3:' 'Case 3'

    # Case 4: the same broken file packed somewhere MSBuild does not import from -> 0. The rule is
    # about the folders a consumer's build imports, not about every file in a package: a malformed
    # XML document under content/ or lib/ breaks nobody's build.
    Reset-Tree
    New-TextFile 'Sdk/assets/Sdk.targets' $dashTargets | Out-Null
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @(
        '<None Include="assets\Sdk.targets" Pack="true" PackagePath="contentFiles\"/>',
        '<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 4 (malformed file packed outside a build folder) expected 0" }
    Assert-Says '1 packed MSBuild file(s) from 1 entr(ies)' 'Case 4'

    # Case 5: buildMultiTargeting is imported too, and a broken file there fails. Case 1b covers
    # buildTransitive on the passing side; without this one, two of the three folders are named in
    # the script and exercised by nothing.
    Reset-Tree
    New-TextFile 'Sdk/buildMultiTargeting/Sdk.targets' $dashTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="buildMultiTargeting\Sdk.targets" Pack="true" PackagePath="buildMultiTargeting\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (broken file under buildMultiTargeting) expected 1" }
    Assert-Says 'Sdk/buildMultiTargeting/Sdk.targets:2:' 'Case 5'

    # Case 6: an Include naming a file that is not there -> 1. The package then ships without the
    # import it declares, which a consumer meets as a missing target rather than a parse error;
    # the gate that resolves the path is the only thing positioned to see it.
    Reset-Tree
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\Gone.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 6 (Include pointing at nothing) expected 1" }
    Assert-Says "packs 'build\Gone.targets' under 'build\' and no such file exists" 'Case 6'

    # Case 7: a wildcard Include -> every file it matches is parsed, and one broken among several
    # fails. An Include is a glob in MSBuild, so a gate that only handles literal paths covers the
    # entry this repository has today and nothing anyone adds tomorrow.
    Reset-Tree
    New-TextFile 'Sdk/build/A.targets' $goodTargets | Out-Null
    New-TextFile 'Sdk/build/B.targets' $dashTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\*.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (wildcard Include, one broken) expected 1" }
    Assert-Says 'Sdk/build/B.targets:2:' 'Case 7'
    Assert-Silent 'Sdk/build/A.targets:' 'Case 7'

    # Case 8: an Include built from an MSBuild property cannot be resolved by a file scan. It is
    # NAMED rather than dropped: unresolved coverage a reader cannot see is coverage they assume.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @(
        '<None Include="$(IntermediateOutputPath)Generated.targets" Pack="true" PackagePath="build\"/>',
        '<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 8 (a property-valued Include) expected 0" }
    Assert-Says 'names an MSBuild property, so this scan cannot resolve it' 'Case 8'
    Assert-Says '1 packed MSBuild file(s) from 2 entr(ies)' 'Case 8'

    # Case 9: a payload under build/ that MSBuild does not import is not parsed, and says so. A
    # build folder may carry one; claiming to have checked it would be a false green.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-TextFile 'Sdk/build/notes.txt' 'this is not XML < & >' | Out-Null
    New-Project 'Sdk/Sdk.csproj' @(
        '<None Include="build\notes.txt" Pack="true" PackagePath="build\"/>',
        '<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 9 (a non-MSBuild payload under build/) expected 0" }
    Assert-Says "'Sdk/build/notes.txt' is packed under a build folder and is not an MSBuild kind - not parsed" 'Case 9'

    # Case 10: the project file itself does not parse -> 1. The derivation reads the csproj as XML,
    # so a malformed one would otherwise be skipped silently and take its packed entry with it -
    # the same defect one step earlier, reported instead of swallowed.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Ok/Ok.csproj' @('<None Include="..\Sdk\build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    New-TextFile 'Sdk/Sdk.csproj' "<Project>`n    <ItemGroup>`n</Project>`n" | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 10 (a malformed project file) expected 1" }
    Assert-Says 'the project file itself is not well-formed XML' 'Case 10'

    # Case 11: the floor for a scan that reached no project file at all. A broken walk reports the
    # same "OK" as a clean repository.
    Reset-Tree
    if ((Invoke-Lint) -ne 1) { throw "Case 11 (no project files) expected 1" }
    Assert-Says 'ZERO project file(s) reached the scan' 'Case 11'

    # Case 12: the floor this gate needs most. Projects are there, and none of them declares a
    # packed build entry - which is what a renamed attribute, a changed PackagePath spelling or a
    # narrowed match looks like from the outside. Without it the whole gate goes inert in silence.
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="README.md" Pack="true" PackagePath="/"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 12 (no packed build entry found) expected 1" }
    Assert-Says 'ZERO packed build-folder entr(ies)' 'Case 12'

    # Case 13: the third floor - entries found, and every one of them resolves to something this
    # gate does not parse. The first two floors are both up and the scan judges nothing.
    Reset-Tree
    New-TextFile 'Sdk/build/notes.txt' 'not XML' | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\notes.txt" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 13 (entries resolving to no MSBuild file) expected 1" }
    Assert-Says 'resolved to ZERO MSBuild file(s) to parse' 'Case 13'

    # Case 14: the branch CI actually takes. Every case above walks the tree, because a temp
    # fixture is not a git repo - so `git ls-files`, the only path spec-gates.yml ever reaches, is
    # covered by none of them. A repo here means tracked project files are the scan's input.
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Host 'packed-msbuild-lint.tests: SKIPPED (git not on PATH; the git-mode case cannot run)'
        exit 0
    }
    Reset-Tree
    New-TextFile 'Sdk/build/Sdk.targets' $goodTargets | Out-Null
    New-Project 'Sdk/Sdk.csproj' @('<None Include="build\Sdk.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    Push-Location $tmp
    try {
        git init --quiet 2>&1 | Out-Null
        git add -A 2>&1 | Out-Null
    }
    finally { Pop-Location }
    if ((Invoke-Lint) -ne 0) { throw "Case 14a (clean git tree) expected 0" }
    Assert-Says '1 packed MSBuild file(s) from 1 entr(ies) across 1 project file(s)' 'Case 14a'

    # An untracked project is not the gate's business, and a tracked one whose packed file broke
    # is. Both halves, or the git branch is pinned only on the passing side.
    New-TextFile 'Extra/build/Extra.targets' $dashTargets | Out-Null
    New-Project 'Extra/Extra.csproj' @('<None Include="build\Extra.targets" Pack="true" PackagePath="build\"/>') | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 14b (untracked broken project is not scanned) expected 0" }
    Assert-Says '1 packed MSBuild file(s) from 1 entr(ies) across 1 project file(s)' 'Case 14b'

    Push-Location $tmp
    try { git add -A 2>&1 | Out-Null }
    finally { Pop-Location }
    if ((Invoke-Lint) -ne 1) { throw "Case 14c (tracked broken packed file) expected 1" }
    Assert-Says 'Extra/build/Extra.targets:2:' 'Case 14c'

    Write-Host 'packed-msbuild-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
