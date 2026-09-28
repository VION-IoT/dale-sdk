#requires -Version 7
# Self-test for release-smoke.ps1. Plain pwsh, NOT Pester. Run all:
# `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/release-smoke.tests.ps1`.
#
# The cases live in the script's own -SelfTest, beside the checks they exercise: the refusals, the
# reference assertion and rewrite, the restore, test-result, published-document and resolution
# checks, each over a fixture. The round trip itself needs a packed release and runs only where one
# exists: publish.yml on a release tag, release-smoke.yml on a dispatch.
$ErrorActionPreference = 'Stop'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'release-smoke.ps1') -SelfTest
exit $LASTEXITCODE
