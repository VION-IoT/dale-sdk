#requires -Version 7
# Self-test for validate-packages.ps1. Plain pwsh, NOT Pester. Run all:
# `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/validate-packages.tests.ps1`.
#
# The cases live in the script's own -SelfTest, beside the derivation they exercise. They build
# throwaway git repositories in a temp directory and never pack: the pack is the .NET SDK's package
# validation, which publish.yml's package-validation job runs against the real tags.
$ErrorActionPreference = 'Stop'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'validate-packages.ps1') -SelfTest
exit $LASTEXITCODE
