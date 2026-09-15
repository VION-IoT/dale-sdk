#requires -Version 7
# Self-test for verify-packed-assembly-versions.ps1. Plain pwsh, NOT Pester. Run all:
# `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/verify-packed-assembly-versions.tests.ps1`.
#
# The cases live in the script's own -SelfTest, beside the rules they exercise; this file is how
# run-script-tests.ps1 finds them, which puts them in spec-gates.yml: every push to main and every pull
# request that changes a script or a project file, with no build and no wait for the pack job the gate
# itself reads from.
$ErrorActionPreference = 'Stop'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify-packed-assembly-versions.ps1') -SelfTest
exit $LASTEXITCODE
