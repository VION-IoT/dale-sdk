#requires -Version 7
# Self-test for spec-trace.ps1 (trace ratchet + quoted-literal coverage). Plain pwsh,
# NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/spec-trace.tests.ps1`.
$ErrorActionPreference = 'Stop'
$trace = Join-Path $PSScriptRoot 'spec-trace.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("spectrace-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, $content) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    Set-Content -LiteralPath $p -Value $content -NoNewline
    return $p
}
function Invoke-Trace {
    pwsh -NoProfile -File $trace -RepoRoot $tmp | Out-Null
    return $LASTEXITCODE
}

try {
    # Case 1: corpus present but nothing traced -> 0 (skip)
    New-File 'docs/specs/emission.md' "# Emission`n- ``AC-EMIT-001.1`` (Event-driven): WHEN x THE SYSTEM SHALL y." | Out-Null
    if ((Invoke-Trace) -ne 0) { throw "Case 1 (no traced pages) expected 0" }

    # Case 2: traced page, id cited as a quoted literal in a *.Test project -> 0.
    # A bare comment mention must NOT count (the test file below also carries one
    # for an id that stays uncovered in Case 3).
    $page = New-File 'docs/specs/plugin.md' @'
---
trace: enforced
---
# Plugin loading
- `AC-PLUG-001.1` (Event-driven): WHEN a shared assembly is requested THE SYSTEM SHALL resolve one instance.
'@
    $test = New-File 'Fake.Sdk.Test/PluginShould.cs' @'
[TestClass]
public class PluginShould
{
    [TestMethod]
    [TestProperty("spec", "AC-PLUG-001.1")]
    public void ResolveOneInstance() { }
    // AC-PLUG-002.1 mentioned in a comment only - must not count as coverage
}
'@
    if ((Invoke-Trace) -ne 0) { throw "Case 2 (cited id) expected 0" }

    # Case 3: an id whose only mention is a comment -> 1 (orphan)
    Set-Content -LiteralPath $page -NoNewline -Value @'
---
trace: enforced
---
- `AC-PLUG-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
- `AC-PLUG-002.1` (Event-driven): WHEN z THE SYSTEM SHALL w.
'@
    if ((Invoke-Trace) -ne 1) { throw "Case 3 (comment-only mention) expected 1" }

    # Case 4: umbrella parent id is covered by its cited leaf -> 0
    Set-Content -LiteralPath $page -NoNewline -Value @'
---
trace: enforced
---
`AC-PLUG-001` umbrella:
- `AC-PLUG-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
'@
    if ((Invoke-Trace) -ne 0) { throw "Case 4 (umbrella via leaf) expected 0" }

    # Case 5: traced page with zero parseable ids -> 1 (anti-vacuous floor)
    $empty = New-File 'docs/specs/lifecycle.md' "---`ntrace: enforced`n---`n# Lifecycle`nprose only"
    if ((Invoke-Trace) -ne 1) { throw "Case 5 (anti-vacuous floor) expected 1" }
    Remove-Item $empty

    # Case 6: in-flight change-doc delta demands a test before distill -> 1, then 0 once cited
    $cd = New-File 'docs/changes/2026-01-01-x.md' @'
---
slug: x
status: in-flight
---
- ADDED AC-PLUG-003.1 -> docs/specs/plugin.md : WHEN q THE SYSTEM SHALL r.
'@
    if ((Invoke-Trace) -ne 1) { throw "Case 6 (in-flight delta uncovered) expected 1" }
    Add-Content -LiteralPath $test -Value "`n// next line is a real citation`nclass A { void B() { var s = `"AC-PLUG-003.1`"; } }"
    if ((Invoke-Trace) -ne 0) { throw "Case 6b (in-flight delta cited) expected 0" }

    # Case 7: REMOVED delta un-declares an id the page still carries -> 0 without a test
    Set-Content -LiteralPath $cd -NoNewline -Value @'
---
slug: x
status: in-flight
---
- REMOVED AC-PLUG-004.1 -> docs/specs/plugin.md : retired
'@
    Add-Content -LiteralPath $page -Value "`n- ``AC-PLUG-004.1`` (Event-driven): WHEN old THE SYSTEM SHALL old."
    if ((Invoke-Trace) -ne 0) { throw "Case 7 (REMOVED exempts) expected 0" }
    Remove-Item $cd

    # Case 8: a scenario file's quoted specs entry counts (SmokeHost root)
    Set-Content -LiteralPath $page -NoNewline -Value @'
---
trace: enforced
---
- `AC-PLUG-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
- `AC-PLUG-005.1` (Event-driven): WHEN s THE SYSTEM SHALL t.
'@
    New-File 'Vion.Dale.DevHost.SmokeHost/scenarios/a.scenario.json' '{ "specs": ["AC-PLUG-005.1"] }' | Out-Null
    if ((Invoke-Trace) -ne 0) { throw "Case 8 (scenario citation) expected 0" }

    # Case 9: a NESTED *.Test root (the xunit projects live under examples/<name>/<name>.Test)
    Add-Content -LiteralPath $page -Value "`n- ``AC-PLUG-006.1`` (Event-driven): WHEN n THE SYSTEM SHALL m."
    if ((Invoke-Trace) -ne 1) { throw "Case 9 pre (uncited nested id) expected 1" }
    New-File 'examples/Fake.Example/Fake.Example.Test/NestedShould.cs' @'
public class NestedShould
{
    [Trait("spec", "AC-PLUG-006.1")]
    public void CoverNestedId() { }
}
'@ | Out-Null
    if ((Invoke-Trace) -ne 0) { throw "Case 9 (nested xunit citation) expected 0" }

    # Case 10: a GAP-marked id needs no test (exempt-but-counted, never an orphan)
    Add-Content -LiteralPath $page -Value "`n- ``AC-PLUG-007.1`` (Event-driven): WHEN g THE SYSTEM SHALL h. GAP: test pending (VION-999)"
    if ((Invoke-Trace) -ne 0) { throw "Case 10 (GAP row exempt) expected 0" }

    Write-Host 'spec-trace.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
