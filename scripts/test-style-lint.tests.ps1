#requires -Version 7
# Self-test for test-style-lint.ps1 (§12 names + §13 markers, ratcheting on spec citations).
# Plain pwsh, NOT Pester. Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/test-style-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'test-style-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("teststyle-" + [guid]::NewGuid().ToString('N'))

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
function Invoke-LintExempting($prefix) {
    pwsh -NoProfile -Command "& '$lint' -RepoRoot '$tmp' -Exempt @{ '$prefix' = 'self-test' }" | Out-Null
    return $LASTEXITCODE
}

try {
    # Case 1: a cited, conforming test (MSTest) + an uncited legacy test with articles and no markers -> 0
    $file = New-File 'Fake.Sdk.Test/GateShould.cs' @'
[TestClass]
public class GateShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-001.1")]
    public void RefuseDriveWhenUnmapped()
    {
        // Arrange
        // Act
        // Assert
    }

    [TestMethod]
    public void ReturnTheValueWhenTheGateIsOpen()
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (conforming cited + uncited legacy) expected 0" }

    # Case 2: a cited test whose name carries an article -> 1
    New-File 'Fake.Sdk.Test/NamesShould.cs' @'
public class NamesShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-002.1")]
    public void ReturnTheValue()
    {
        // Act
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (cited name with article) expected 1" }
    Remove-Item (Join-Path $tmp 'Fake.Sdk.Test/NamesShould.cs')

    # Case 3: a cited test with no markers -> 1; the combined marker forms -> 0
    $m = New-File 'Fake.Sdk.Test/MarkersShould.cs' @'
public class MarkersShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-003.1")]
    public void ApplyPolicy()
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 3 (cited, no markers) expected 1" }
    Set-Content -LiteralPath $m -NoNewline -Value @'
public class MarkersShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-003.1")]
    public void ApplyPolicy()
    {
        // Arrange / Act
        var x = 1;
        // Assert
    }

    [TestMethod]
    [TestProperty("spec", "AC-GATE-003.2")]
    public void ThrowWhenPortInUse()
    {
        // Act / Assert
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 3b (combined marker forms) expected 0" }

    # Case 4: xunit [Trait] citation in a nested example project; words that merely START with
    # A/An/The/Is (Advance, Analyzer, Theme, Issue) must not trip the article check -> 0
    New-File 'examples/Fake.Example/Fake.Example.Test/AdvanceShould.cs' @'
public class AdvanceShould
{
    [Fact]
    [Trait("spec", "AC-SCEN-001.1")]
    public void AdvanceAnalyzerThemeIssueWhenStepped()
    {
        // Arrange
        // Act
        // Assert
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 4 (xunit nested, prefix words) expected 0" }

    # Case 5: the marker check is per method - a conforming neighbour does not cover a bare one -> 1
    New-File 'Fake.Sdk.Test/PairShould.cs' @'
public class PairShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-004.1")]
    public void KeepFirst()
    {
        // Act
    }

    [TestMethod]
    [TestProperty("spec", "AC-GATE-004.2")]
    public void KeepSecond()
    {
        Assert.IsTrue(true);
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (per-method markers) expected 1" }

    Remove-Item (Join-Path $tmp 'Fake.Sdk.Test/PairShould.cs')

    # Case 6: a cited, non-conforming test in an exempt project is skipped -> 0, and the SAME file
    # fails once the exemption is gone -> 1. The built-in list is empty (the ANLZ pass retired its last
    # entry), so the exemption is seeded here rather than borrowed from a live one — otherwise this
    # case would pass for the wrong reason the moment the list emptied, which is what it just did.
    New-File 'Other.Area.Test/SomeAnalyzerTests.cs' @'
public class SomeAnalyzerTests
{
    [TestMethod]
    [TestProperty("spec", "AC-EMIT-012.1")]
    public void MinChangeWithTheDefault_NoDiagnostic()
    {
        Assert.IsTrue(true);
    }
}
'@ | Out-Null
    if ((Invoke-LintExempting 'Other.Area.Test/') -ne 0) { throw "Case 6 (exempt project) expected 0" }
    if ((Invoke-Lint) -ne 1) { throw "Case 6 (the same file, unexempted) expected 1" }

    Write-Host 'test-style-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
