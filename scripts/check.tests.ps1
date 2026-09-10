#requires -Version 7
# Self-test for check.ps1 (run every gate spec-gates.yml runs, one line each). Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/check.tests.ps1`.
#
# check.ps1 shells out to the nine real gates, so the interesting part is not the shelling: it
# is the derivation from the workflow, what happens when the workflow and the invocation table
# disagree, the arguments each gate receives, and the two -CiShape checks. All of that runs
# against a fixture repository with fake gates, so no real gate's verdict is involved. It is
# not cheap: each case spawns check.ps1, which spawns nine more pwsh processes, so the suite
# costs ~40s - about half of what run-script-tests.ps1 takes in total.
$ErrorActionPreference = 'Stop'
$check = Join-Path $PSScriptRoot 'check.ps1'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host 'check.tests: SKIPPED (git not on PATH; the fixture needs an index and a base ref)'
    exit 0
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("check-" + [guid]::NewGuid().ToString('N'))
$scriptsDir = Join-Path $tmp 'scripts'
$workflow = Join-Path $tmp '.github/workflows/spec-gates.yml'

# The nine gate names spec-gates.yml runs today, in its order. The fixture reuses the real
# names because check.ps1's invocation table is keyed on them.
$gates = @(
    'run-script-tests', 'spec-lint', 'spec-trace', 'test-style-lint', 'doc-comment-lint',
    'pragma-reason-lint', 'bom-lint', 'journal-lint', 'sweep-residue-lint'
)

function Write-File([string]$path, [string[]]$lines) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    [System.IO.File]::WriteAllLines($path, [string[]]$lines, [System.Text.UTF8Encoding]::new($false))
}

# A stand-in gate: accepts the arguments check.ps1 passes, ECHOES them so a case can assert on
# what it actually received, prints its own summary line the way the real gates do, and exits
# with the code the fixture asked for. Without the echo, dropping an argument in check.ps1 is a
# silent no-op here - every real gate exits 0 whether or not it gets one.
function Write-Gate([string]$name, [int]$exit) {
    Write-File (Join-Path $scriptsDir "$name.ps1") @(
        'param([string]$RepoRoot, [string]$Diff)',
        "Write-Host ('${name}: args RepoRoot=[' + `$RepoRoot + '] Diff=[' + `$Diff + ']')",
        "Write-Host '${name}: OK - fixture'",
        "exit $exit"
    )
}

# A workflow naming the given scripts as steps, in the given order, in spec-gates.yml's shape.
function Write-Workflow([string[]]$names) {
    $lines = @('name: Spec Gates', 'jobs:', '  spec-gates:', '    steps:')
    foreach ($n in $names) {
        $lines += @("      - name: $n", '        shell: pwsh', "        run: ./scripts/$n.ps1")
    }
    Write-File $workflow $lines
}

function Invoke-Check([string[]]$extra) {
    $script:out = & pwsh -NoProfile -File $check -RepoRoot $tmp @extra 2>&1 | Out-String
    return $LASTEXITCODE
}

function Expect([int]$code, [string]$case, [string[]]$mentions, [string[]]$extra = @()) {
    $rc = Invoke-Check $extra
    if ($rc -ne $code) { throw "$case expected exit $code, got $rc`n$script:out" }
    foreach ($m in $mentions) {
        if ($script:out -notmatch [regex]::Escape($m)) { throw "$case expected the output to mention '$m'`n$script:out" }
    }
}

try {
    New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
    git -C $tmp init --quiet 2>&1 | Out-Null
    git -C $tmp config user.email 'check-tests@example.invalid' | Out-Null
    git -C $tmp config user.name 'check tests' | Out-Null

    foreach ($g in $gates) { Write-Gate $g 0 }
    Write-Workflow $gates

    # A tracked file the path-case cases below name. Its casing is the index's, so a literal
    # spelling it differently is the defect the scan exists for.
    Write-File (Join-Path $tmp 'Vion.Dale.Sdk/Foo.cs') @('// fixture')
    # A dotless tracked directory for the bare-word case below. Named nothing this repository
    # has, so the literal spelling its wrong casing cannot match a real path from this file.
    Write-File (Join-Path $tmp 'plainly/Bar.cs') @('// fixture')
    git -C $tmp add -A 2>&1 | Out-Null
    git -C $tmp commit -m 'fixture' --quiet 2>&1 | Out-Null
    # What CI diffs against, and what check.ps1 requires before it will run spec-lint.
    git -C $tmp update-ref refs/remotes/origin/main HEAD

    # Case 1: nine green gates -> exit 0, one PASS line each, build and test skipped unasked.
    Expect 0 'Case 1 (all green)' @(
        'check: 9 gate(s), derived from .github/workflows/spec-gates.yml',
        'PASS  run-script-tests', 'PASS  sweep-residue-lint',
        'SKIP  build', 'not requested (pass -Build)',
        'SKIP  test', 'not requested (pass -Test)',
        'check: OK - 9 step(s) passed, 2 skipped')
    # The summary line each gate prints is what lands on its line, not its first line.
    if ($script:out -notmatch 'PASS  bom-lint\s+\d') { throw "Case 1 expected a timed bom-lint line`n$script:out" }
    if ($script:out -match 'args RepoRoot=') { throw "Case 1 should carry each gate's LAST line, not its first`n$script:out" }
    if ($script:out -match 'PARTIAL') { throw "Case 1 has a resolvable base ref, so nothing should run partially`n$script:out" }

    # Case 1b: a step naming its script twice is one gate, not two. spec-gates.yml's real
    # spec-lint step does exactly this, in an if/else on GITHUB_BASE_REF.
    $ifElse = @('name: Spec Gates', 'jobs:', '  spec-gates:', '    steps:')
    foreach ($n in $gates) {
        if ($n -eq 'spec-lint') {
            $ifElse += @('      - name: spec-lint', '        run: |',
                '          if ($env:GITHUB_BASE_REF) { ./scripts/spec-lint.ps1 -Diff "origin/$env:GITHUB_BASE_REF" }',
                '          else { ./scripts/spec-lint.ps1 }')
        }
        else { $ifElse += @("      - name: $n", "        run: ./scripts/$n.ps1") }
    }
    Write-File $workflow $ifElse
    Expect 0 'Case 1b (a step naming its script twice)' @('check: 9 gate(s)', 'check: OK - 9 step(s) passed, 2 skipped')
    if (@([regex]::Matches($script:out, '(?m)^\s+PASS\s+spec-lint\s')).Count -ne 1) {
        throw "Case 1b expected one spec-lint line`n$script:out"
    }
    Write-Workflow $gates

    # Case 1c: a script named in a workflow COMMENT is not a gate. spec-gates.yml comments
    # every step, and a comment pointing at a script the workflow does not run would otherwise
    # mint a gate that then fails for having no local invocation.
    $commented = @('name: Spec Gates', '# See also ./scripts/cleanup-code.ps1, which the style gate runs elsewhere.',
        'jobs:', '  spec-gates:', '    steps:')
    foreach ($n in $gates) { $commented += @("      - name: $n", "        run: ./scripts/$n.ps1") }
    Write-File $workflow $commented
    Expect 0 'Case 1c (a script named in a comment)' @('check: 9 gate(s)', 'check: OK - 9 step(s) passed, 2 skipped')
    if ($script:out -match 'cleanup-code') { throw "Case 1c minted a gate from a comment`n$script:out" }
    Write-Workflow $gates

    # Case 2: the order printed is the workflow's, not the invocation table's.
    $reordered = @('bom-lint', 'journal-lint') + ($gates | Where-Object { $_ -notin @('bom-lint', 'journal-lint') })
    Write-Workflow $reordered
    Expect 0 'Case 2 (workflow order)' @('check: OK')
    $printed = @([regex]::Matches($script:out, '(?m)^\s+PASS\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
    if (($printed -join ',') -ne ($reordered -join ',')) {
        throw "Case 2 expected the workflow's order '$($reordered -join ',')', got '$($printed -join ',')'`n$script:out"
    }
    Write-Workflow $gates

    # Case 3: one gate red -> exit 1, named in the tail, its full output echoed below the table.
    Write-Gate 'bom-lint' 1
    Expect 1 'Case 3 (one gate red)' @(
        'FAIL  bom-lint', '--- bom-lint ---', 'bom-lint: args RepoRoot=',
        'check: FAIL - 1 of 11 step(s) failed: bom-lint')
    Write-Gate 'bom-lint' 0

    # Case 3b: spec-lint gets the base ref CI gives it. This is the one argument that makes a
    # desk run equivalent to CI's, and dropping it is otherwise invisible - spec-lint without
    # -Diff still exits 0 and still reports PASS.
    Write-Gate 'spec-lint' 1
    Expect 1 'Case 3b (spec-lint arguments)' @("spec-lint: args RepoRoot=[$tmp] Diff=[origin/main]")
    Write-Gate 'spec-lint' 0

    # Case 4: the workflow gained a gate check.ps1 has no local invocation for -> exit 1. This
    # is the drift the derivation exists to catch: the gate would otherwise not run at all.
    Write-Gate 'brand-new-lint' 0
    Write-Workflow ($gates + 'brand-new-lint')
    Expect 1 'Case 4 (unknown gate)' @('FAIL  brand-new-lint', 'knows no local invocation for it')
    Remove-Item -LiteralPath (Join-Path $scriptsDir 'brand-new-lint.ps1')
    Write-Workflow $gates

    # Case 5: a gate check.ps1 does know, whose script is gone -> exit 1, said plainly.
    Remove-Item -LiteralPath (Join-Path $scriptsDir 'bom-lint.ps1')
    Expect 1 'Case 5 (missing script)' @('FAIL  bom-lint', 'no such script exists')
    Write-Gate 'bom-lint' 0

    # Case 6: the base ref does not resolve -> spec-lint still RUNS, without -Diff, reported
    # PARTIAL with what did not run. Skipping the gate would hide its eight other rules on
    # every unfetched clone; passing the ref anyway would make one rule a vacuous pass.
    git -C $tmp update-ref -d refs/remotes/origin/main
    Expect 0 'Case 6 (no base ref)' @(
        'PASS  spec-lint', 'PARTIAL: origin/main does not resolve',
        'the narrative rule needs a base ref; its other rules ran', 'git fetch origin',
        'check: OK - 9 step(s) passed, 2 skipped; 1 ran partially: spec-lint')

    # Case 6b: and it really did run without the ref, rather than with an unresolvable one.
    Write-Gate 'spec-lint' 1
    Expect 1 'Case 6b (no base ref, arguments)' @("spec-lint: args RepoRoot=[$tmp] Diff=[]")
    Write-Gate 'spec-lint' 0
    git -C $tmp update-ref refs/remotes/origin/main HEAD

    # Case 7: -CiShape's path-case scan catches a literal that differs from the index only in
    # case - true for Test-Path on Windows, false on the runner.
    Write-File (Join-Path $scriptsDir 'reader.ps1') @("`$p = 'Vion.Dale.SDK/Foo.cs'", 'Write-Host $p')
    Expect 1 'Case 7 (wrong-case literal)' @(
        'FAIL  path-case', "'Vion.Dale.SDK/Foo.cs' is tracked as 'Vion.Dale.Sdk/Foo.cs'",
        'false on the runner') @('-CiShape')

    # Case 7b: the same wrong casing in comment text is an example, not a call. check.ps1's
    # own help block names 'Vion.Dale.SDK' to explain the defect, and flagged itself for it.
    Write-File (Join-Path $scriptsDir 'reader.ps1') @(
        '<#', "  Reads 'Vion.Dale.SDK/Foo.cs' - the wrong casing, named on purpose.", '#>',
        "# Also wrong on purpose: 'Vion.Dale.SDK/Foo.cs'.",
        "`$p = 'Vion.Dale.Sdk/Foo.cs'", 'Write-Host $p')
    Expect 0 'Case 7b (wrong case in a comment)' @('PASS  path-case') @('-CiShape')

    # Case 7c: a bare directory name with no dot and no separator is compared too. Every
    # top-level directory in this repository is that shape - `docs`, `scripts`, `templates` -
    # and Join-Path takes them as literals throughout scripts/, so a filter that skipped them
    # would blind the scan at the call sites it was written among. The fixture's directory is
    # named `plainly` rather than one of those: the scan reads scripts/*.ps1 including this
    # file, so a wrong-case literal naming a REAL directory here would flag this line.
    Write-File (Join-Path $scriptsDir 'reader.ps1') @("`$p = Join-Path `$r 'Plainly'", 'Write-Host $p')
    Expect 1 'Case 7c (bare directory name, wrong case)' @('FAIL  path-case', "'Plainly' is tracked as 'plainly'") @('-CiShape')

    # Case 8: the same literal in the index's casing passes, and the count says how many
    # literals were actually compared.
    Write-File (Join-Path $scriptsDir 'reader.ps1') @("`$p = 'Vion.Dale.Sdk/Foo.cs'", 'Write-Host $p')
    Expect 0 'Case 8 (right-case literal)' @('PASS  path-case', "path literal(s) in scripts/*.ps1 match the index's casing") @('-CiShape')
    if ($script:out -notmatch 'PASS  path-case\s+\S+\s+([1-9]\d*) path literal') {
        throw "Case 8 expected a non-zero literal count`n$script:out"
    }

    # Case 9: nothing in scripts/ names a tracked path -> the anti-vacuous floor fires rather
    # than the scan reporting a clean run over nothing.
    Remove-Item -LiteralPath (Join-Path $scriptsDir 'reader.ps1')
    git -C $tmp rm --cached -r --quiet '.github' 2>&1 | Out-Null
    Expect 1 'Case 9 (vacuous scan)' @('FAIL  path-case', 'the scan is looking at nothing') @('-CiShape')
    git -C $tmp add -A 2>&1 | Out-Null
    git -C $tmp commit -m 'fixture 2' --quiet 2>&1 | Out-Null
    git -C $tmp update-ref refs/remotes/origin/main HEAD

    # Case 10: -CiShape says what it did to the dot-directories and what it would pass to a
    # build, so a reader can tell a CI-shaped run from a plain one.
    Write-File (Join-Path $scriptsDir 'reader.ps1') @("`$p = 'Vion.Dale.Sdk/Foo.cs'", 'Write-Host $p')
    Expect 0 'Case 10 (CiShape banner)' @('check: -CiShape - ', '-Build/-Test carry -p:Version=0.0.0-ci.1') @('-CiShape')

    # Case 10b: -CiShape's only side effect on the working tree is undone, including on the
    # path a reader would most doubt - a run that ended with a gate red.
    if ($IsWindows) {
        $before = @(Get-ChildItem -LiteralPath $tmp -Directory -Force | Where-Object { $_.Name.StartsWith('.') } |
            ForEach-Object { "$($_.Name)=$([bool]($_.Attributes -band [System.IO.FileAttributes]::Hidden))" } | Sort-Object)
        Write-Gate 'bom-lint' 1
        Expect 1 'Case 10b (attributes restored after a red gate)' @('FAIL  bom-lint') @('-CiShape')
        Write-Gate 'bom-lint' 0
        $after = @(Get-ChildItem -LiteralPath $tmp -Directory -Force | Where-Object { $_.Name.StartsWith('.') } |
            ForEach-Object { "$($_.Name)=$([bool]($_.Attributes -band [System.IO.FileAttributes]::Hidden))" } | Sort-Object)
        if (($before -join ',') -ne ($after -join ',')) {
            throw "Case 10b expected '$($before -join ',')' after the run, got '$($after -join ',')'"
        }
        if ($before -notcontains '.github=False') {
            throw "Case 10b needs a visible dot-directory to be a test at all; got '$($before -join ',')'"
        }
    }

    # Case 11: no workflow to derive from -> exit 2, distinct from a gate failing.
    Remove-Item -LiteralPath $workflow
    Expect 2 'Case 11 (no workflow)' @('nothing to derive the gate list from')

    # Case 12: a workflow with no ./scripts/*.ps1 step -> exit 2. A derivation that silently
    # found nothing would report "0 gate(s)" and exit 0.
    Write-File $workflow @('name: Spec Gates', 'jobs:', '  spec-gates:', '    steps:', '      - run: echo hi')
    Expect 2 'Case 12 (nothing derived)' @('the derivation is broken, not the repository')

    Write-Host 'check.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
