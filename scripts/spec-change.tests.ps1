#requires -Version 7
# Self-test for spec-change.ps1 (scaffold + distill-gated archive). Plain pwsh, NOT
# Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/spec-change.tests.ps1`.
$ErrorActionPreference = 'Stop'
$change = Join-Path $PSScriptRoot 'spec-change.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("specchange-" + [guid]::NewGuid().ToString('N'))
$today = Get-Date -Format 'yyyy-MM-dd'

function New-File($rel, $content) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    Set-Content -LiteralPath $p -Value $content -NoNewline
    return $p
}

try {
    New-File 'docs/changes/_template.md' @'
---
slug: <kebab-case-slug>
status: proposed
areas: <AREA[, AREA]>
created: YYYY-MM-DD
updated: YYYY-MM-DD
---
# <Title>
'@ | Out-Null

    # Case 1: new scaffolds with slug/date/areas filled
    pwsh -NoProfile -File $change new my-change -Areas 'EMIT' -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Case 1 (new) expected 0" }
    $doc = Join-Path $tmp "docs/changes/$today-my-change.md"
    if (-not (Test-Path $doc)) { throw "Case 1: $doc not created" }
    $raw = Get-Content -Raw $doc
    if ($raw -notmatch '(?m)^slug: my-change$')  { throw "Case 1: slug not filled" }
    if ($raw -notmatch '(?m)^areas: EMIT$')      { throw "Case 1: areas not filled" }
    if ($raw -notmatch "(?m)^created: $today$")  { throw "Case 1: created not filled" }

    # Case 2: archive refuses while an ADDED id is not in its .md target
    Set-Content -LiteralPath $doc -NoNewline -Value @'
---
slug: my-change
status: in-flight
---
## Spec delta (to distill)
- ADDED AC-EMIT-001.1 -> docs/specs/emission.md : WHEN x THE SYSTEM SHALL y.
- ADDED gates -> scripts/some-gate.ps1 : the gate script exists
'@
    New-File 'docs/specs/emission.md' '# Emission (id not distilled yet)' | Out-Null
    New-File 'scripts/some-gate.ps1' '# present' | Out-Null
    pwsh -NoProfile -File $change archive my-change -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 1) { throw "Case 2 (undistilled) expected 1" }
    if (-not (Test-Path $doc)) { throw "Case 2: doc must not move on refusal" }

    # Case 3: REMOVED id still present in the target also refuses
    Set-Content -LiteralPath (Join-Path $tmp 'docs/specs/emission.md') -NoNewline -Value @'
# Emission
- `AC-EMIT-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
- `AC-EMIT-009.1` (Event-driven): WHEN old THE SYSTEM SHALL old.
'@
    Add-Content -LiteralPath $doc -Value "`n- REMOVED AC-EMIT-009.1 -> docs/specs/emission.md : retired"
    pwsh -NoProfile -File $change archive my-change -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 1) { throw "Case 3 (REMOVED still present) expected 1" }

    # Case 4: fully distilled -> archived, status flipped, moved to archive/
    Set-Content -LiteralPath (Join-Path $tmp 'docs/specs/emission.md') -NoNewline -Value @'
# Emission
- `AC-EMIT-001.1` (Event-driven): WHEN x THE SYSTEM SHALL y.
'@
    pwsh -NoProfile -File $change archive my-change -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Case 4 (distilled) expected 0" }
    $archived = Join-Path $tmp "docs/changes/archive/$today-my-change.md"
    if (-not (Test-Path $archived)) { throw "Case 4: doc not moved to archive/" }
    if (Test-Path $doc) { throw "Case 4: original path still exists" }
    if ((Get-Content -Raw $archived) -notmatch '(?m)^status: archived$') { throw "Case 4: status not flipped" }

    # Case 4b: the id is on the page but the page's bullet does not carry the delta's text -> refuse, doc stays
    pwsh -NoProfile -File $change new text-drift -RepoRoot $tmp | Out-Null
    $doc3 = Join-Path $tmp "docs/changes/$today-text-drift.md"
    Set-Content -LiteralPath $doc3 -NoNewline -Value @'
---
slug: text-drift
status: in-flight
---
- ADDED AC-GATE-005.9 -> docs/specs/gating.md : WHEN a predicate is empty THE SYSTEM SHALL report the member ungated.
'@
    New-File 'docs/specs/gating.md' @'
# Gating
- `AC-GATE-005.9` (Event-driven): WHEN a predicate is empty THE SYSTEM SHALL refuse the gate as one
  that cannot be resolved.
- `AC-GATE-005.10` (Event-driven): WHEN a predicate is empty THE SYSTEM SHALL report the member ungated.
'@ | Out-Null
    pwsh -NoProfile -File $change archive text-drift -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 1) { throw "Case 4b (page text differs from delta; the .10 sibling must not stand in) expected 1" }
    if (-not (Test-Path $doc3)) { throw "Case 4b: doc must not move on refusal" }

    # Case 4c: a MODIFIED line below the ADDED one supersedes it; the page carries the MODIFIED text
    # wrapped and backticked, the delta carries a trailing provenance parenthetical -> archived
    Add-Content -LiteralPath $doc3 -Value "`n- MODIFIED ``AC-GATE-005.9`` -> docs/specs/gating.md : WHEN a predicate is empty THE SYSTEM SHALL refuse the gate as one that cannot be resolved. (row 64 fix)"
    pwsh -NoProfile -File $change archive text-drift -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Case 4c (MODIFIED supersedes ADDED; formatting set aside) expected 0" }

    # Case 4d: the id present only in prose, with no declaring bullet -> refuse
    pwsh -NoProfile -File $change new no-bullet -RepoRoot $tmp | Out-Null
    $doc4 = Join-Path $tmp "docs/changes/$today-no-bullet.md"
    Set-Content -LiteralPath $doc4 -NoNewline -Value @'
---
slug: no-bullet
status: in-flight
---
- ADDED AC-GATE-006.1 -> docs/specs/gating.md : THE SYSTEM SHALL evaluate nothing while introspecting.
'@
    Add-Content -LiteralPath (Join-Path $tmp 'docs/specs/gating.md') -Value "`nAs ``AC-GATE-006.1`` says, nothing is evaluated while introspecting."
    pwsh -NoProfile -File $change archive no-bullet -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 1) { throw "Case 4d (id in prose, no declaring bullet) expected 1" }

    # Case 5: a non-.md target (a script) satisfies ADDED by existing — covered in Case 4

    # (scripts/some-gate.ps1); its absence must refuse.
    pwsh -NoProfile -File $change new second -RepoRoot $tmp | Out-Null
    $doc2 = Join-Path $tmp "docs/changes/$today-second.md"
    Set-Content -LiteralPath $doc2 -NoNewline -Value @'
---
slug: second
status: in-flight
---
- ADDED thing -> scripts/missing.ps1 : must exist
'@
    pwsh -NoProfile -File $change archive second -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 1) { throw "Case 5 (missing non-md target) expected 1" }

    # Case 6: usage errors exit 2, distinct from the exit-1 "not distilled" refusal
    pwsh -NoProfile -File $change archive no-such-slug -RepoRoot $tmp | Out-Null
    if ($LASTEXITCODE -ne 2) { throw "Case 6 (unknown slug) expected 2" }
    $bare = Join-Path $tmp 'bare'
    New-Item -ItemType Directory -Force -Path (Join-Path $bare 'docs/changes') | Out-Null
    pwsh -NoProfile -File $change new x -RepoRoot $bare | Out-Null
    if ($LASTEXITCODE -ne 2) { throw "Case 6b (missing template) expected 2" }

    Write-Host 'spec-change.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
