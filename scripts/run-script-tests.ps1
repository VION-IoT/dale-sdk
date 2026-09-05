#requires -Version 7
<#
.SYNOPSIS
  Discover and run every scripts/*.tests.ps1 self-test, AND enforce that every gate/tool
  script is tested-or-exempt. Fails if any self-test fails or any script is silently
  untested. Wired into CI (spec-gates.yml). Ported from logic-block-libraries.
.DESCRIPTION
  Each *.tests.ps1 is a standalone PowerShell script (deliberately NOT Pester): it builds
  a temp fixture, invokes its sibling gate script as a child process, asserts the exit
  code / output, and signals the result via its OWN exit code -

    exit 0 + "<name>.tests: PASS"      -> passed
    exit 0 + "<name>.tests: SKIPPED"   -> skipped (e.g. git not on PATH)
    thrown error (non-zero exit)       -> failed

  This runner runs each in its OWN pwsh process (isolating cwd / $ErrorActionPreference /
  temp cleanup), prints one line per test, and dumps full output only on failure. New
  *.tests.ps1 files are picked up automatically.

  META-GATE: it then checks that every scripts/*.ps1 either has a sibling *.tests.ps1 or
  is in the $exempt list below with a reason — so a gate shipped without a self-test
  fails here instead of silently passing CI.
.EXAMPLE
  pwsh -File scripts/run-script-tests.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Scripts intentionally without a *.tests.ps1, each with the reason. Anything NOT here and
# NOT tested fails the meta-gate below — this list is the explicit, reviewable record of
# what is deliberately untested (not an accident).
$exempt = @{
    'run-script-tests.ps1'                 = 'the runner itself (a self-test would be circular)'
    'cleanup-code.ps1'                     = 'wrapper around `dotnet jb cleanupcode`; the logic under test is the external tool, not runnable in a fast self-test (needs the pinned tool + a built solution)'
    'pack-examples.ps1'                    = 'on-demand pack helper; needs a full dotnet build + feeds, not a fast self-test'
    'smoke-modbus.ps1'                     = 'driver for the modbus-smoke skill; needs a live DevHost on a real socket pair, not a CI gate'
    'stage-xml-docs.ps1'                   = 'CI staging helper over packed .nupkg artifacts; its input is the publish job''s output, not reproducible in a fast fixture'
    'verify-packed-assembly-versions.ps1'  = 'carries its own built-in self-test (-SelfTest), run by the verify-packages CI job'
}

$tests = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.tests.ps1' | Sort-Object Name

$failed = @()
$skipped = 0
foreach ($t in $tests) {
    $output = & pwsh -NoProfile -File $t.FullName 2>&1
    $rc = $LASTEXITCODE
    if ($rc -ne 0) {
        Write-Host "  FAIL  $($t.Name)"
        $output | ForEach-Object { Write-Host "        $_" }
        $failed += $t.Name
    }
    elseif (($output -join "`n") -match 'SKIPPED') {
        Write-Host "  SKIP  $($t.Name)"
        $skipped++
    }
    else {
        Write-Host "  PASS  $($t.Name)"
    }
}

# Meta-gate: every non-test script is tested or exempt.
$tested = @{}
$tests | ForEach-Object { $tested[($_.Name -replace '\.tests\.ps1$', '.ps1')] = $true }
$uncovered = @()
foreach ($s in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' | Where-Object { $_.Name -notlike '*.tests.ps1' } | Sort-Object Name)) {
    if ($tested.ContainsKey($s.Name) -or $exempt.ContainsKey($s.Name)) { continue }
    $uncovered += $s.Name
}
$staleExempt = @($exempt.Keys | Where-Object { -not (Test-Path (Join-Path $PSScriptRoot $_)) } | Sort-Object)
$exemptButTested = @($exempt.Keys | Where-Object { $tested.ContainsKey($_) } | Sort-Object)

Write-Host ''
$staleExempt | ForEach-Object { Write-Host "  note: exempt entry '$_' has no matching script — remove it from `$exempt" }
$exemptButTested | ForEach-Object { Write-Host "  note: exempt entry '$_' now HAS a self-test — remove it from `$exempt" }

$ok = $true
if ($failed.Count) {
    Write-Host "run-script-tests: FAIL - $($failed.Count) of $($tests.Count) self-test(s) failed:"
    $failed | ForEach-Object { Write-Host "  $_" }
    $ok = $false
}
if ($uncovered.Count) {
    Write-Host "run-script-tests: FAIL - $($uncovered.Count) gate/tool script(s) have no self-test and are not exempt:"
    $uncovered | ForEach-Object {
        $stub = ($_ -replace '\.ps1$', '') + '.tests.ps1'
        Write-Host "  $_ — add scripts/$stub, or add it to `$exempt in run-script-tests.ps1 with a reason"
    }
    $ok = $false
}
if (-not $ok) { exit 1 }

$tail = if ($skipped) { " ($skipped skipped)" } else { '' }
Write-Host "run-script-tests: OK - $($tests.Count) self-test(s) passed$tail; $($exempt.Count) script(s) exempt"
exit 0
