#requires -Version 7
# Self-test for spec-lint.ps1 (EARS grammar + change-doc lifecycle). Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/spec-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'spec-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("speclint-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, $content) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    Set-Content -LiteralPath $p -Value $content -NoNewline
    return $p
}
function Invoke-Lint {
    pwsh -NoProfile -File $lint -RepoRoot $tmp | Out-Null
    return $LASTEXITCODE
}

try {
    # Case 1: clean corpus page + archived doc in archive/ + in-flight doc at top level -> 0
    $page = New-File 'docs/specs/emission.md' @'
---
trace: enforced
---
# Emission
- `AC-EMIT-001.1` (Event-driven): WHEN a service property changes THE SYSTEM SHALL publish it.
- `AC-EMIT-001.2` (State-driven): WHILE throttled THE SYSTEM SHALL,
  spanning two lines, coalesce to the latest value.
'@
    New-File 'docs/changes/2026-01-01-live.md' "---`nslug: live`nstatus: in-flight`nblocked-on: none`n---`nbody" | Out-Null
    New-File 'docs/changes/archive/2026-01-01-done.md' "---`nslug: done`nstatus: archived`nblocked-on: none`n---`nbody" | Out-Null
    New-File 'docs/changes/_template.md' "---`nslug: <kebab-case-slug>`nstatus: proposed`n---`nbody" | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (clean) expected 0" }

    # Case 2: escape-hatch word -> 1; a hyphenated domain term must NOT trip it
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-EMIT-001.1` (Event-driven): WHEN x THE SYSTEM SHALL be fast.
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (escape-hatch) expected 1" }
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-EMIT-001.1` (Event-driven): WHEN the fast-charge flag is set THE SYSTEM SHALL react.
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 2b (hyphenated compound) expected 0" }

    # Case 3: malformed declared id (lowercase area) -> 1
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-emit-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 3 (malformed id) expected 1" }

    # Case 4: AC without a SHALL -> 1; a lowercase 'shall' is prose, not the predicate -> 1
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-EMIT-001.1` (Event-driven): WHEN x the system publishes y.
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 4 (no SHALL) expected 1" }
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-EMIT-001.1` (Event-driven): WHEN x the system shall publish y.
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 4b (lowercase shall) expected 1" }
    Set-Content -LiteralPath $page -NoNewline -Value @'
- `AC-EMIT-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
'@

    # Case 5: status archived outside archive/ -> 1
    $stray = New-File 'docs/changes/2026-01-02-stray.md' "---`nslug: stray`nstatus: archived`n---`nbody"
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (archived at top level) expected 1" }
    Remove-Item $stray

    # Case 6: unknown status -> 1
    $bad = New-File 'docs/changes/2026-01-03-bad.md' "---`nslug: bad`nstatus: wip`n---`nbody"
    if ((Invoke-Lint) -ne 1) { throw "Case 6 (unknown status) expected 1" }
    Remove-Item $bad

    # Case 7: parked without a blocked-on reason -> 1; with one -> 0
    $parked = New-File 'docs/changes/2026-01-04-parked.md' "---`nslug: parked`nstatus: parked`nblocked-on: none`n---`nbody"
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (parked, no reason) expected 1" }
    Set-Content -LiteralPath $parked -NoNewline -Value "---`nslug: parked`nstatus: parked`nblocked-on: waiting on VION-99`n---`nbody"
    if ((Invoke-Lint) -ne 0) { throw "Case 7b (parked with reason) expected 0" }

    Write-Host 'spec-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
