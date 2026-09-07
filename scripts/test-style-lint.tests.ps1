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

    Remove-Item (Join-Path $tmp 'Other.Area.Test/SomeAnalyzerTests.cs')

    # Case 7: a `]` inside a string in the attribute block must not hide the method. Both string forms
    # the suite uses carry one: a quoted literal with escaped quotes
    # (`[DataRow("[ServiceProperty(Title = \"Power\")]")]`) and a raw string literal
    # (`[DataRow("""{ "checks": [] }""")]`). The block regex stopped at the FIRST `]`, so the last
    # attribute line never reached the signature and the whole method went unchecked -> the two
    # violations below (an article, no markers) must be reported.
    $br = New-File 'Fake.Sdk.Test/BracketShould.cs' @'
public class BracketShould
{
    [TestMethod]
    [TestProperty("spec", "AC-CLI-006.11")]
    [DataRow("Mode in ['Eco', 'Fast']", 1)]
    public void EmitTheAnnotation()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    [TestProperty("spec", "AC-SCEN-001.2")]
    [DataRow("""{ "version": 1, "id": "x", "checks": [] }""", DisplayName = "unmapped property")]
    public void RejectUnmappedProperty()
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (a ] inside a string hides the method) expected 1" }

    # Case 7b: the same two attribute blocks over conforming methods -> 0, so the un-hiding does not
    # itself become a false report
    Set-Content -LiteralPath $br -NoNewline -Value @'
public class BracketShould
{
    [TestMethod]
    [TestProperty("spec", "AC-CLI-006.11")]
    [DataRow("Mode in ['Eco', 'Fast']", 1)]
    public void EmitAnnotation()
    {
        // Arrange / Act
        // Assert
    }

    [TestMethod]
    [TestProperty("spec", "AC-SCEN-001.2")]
    [DataRow("""{ "version": 1, "id": "x", "checks": [] }""", DisplayName = "unmapped property")]
    public void RejectUnmappedProperty()
    {
        // Act
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 7b (the same blocks over conforming methods) expected 0" }

    Remove-Item $br

    # Case 8: the other two ways an attribute block fails to reach its signature - a `]` closing a
    # NESTED bracket (`new[] { … }`, a collection expression) and an attribute WRAPPED onto an
    # indented continuation line. Same defect as case 7, different symbol: the method is checked by
    # nothing, so the article and the missing markers below must be reported.
    $w = New-File 'Fake.Sdk.Test/WrapShould.cs' @'
public class WrapShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-006.3")]
    [DataRow(1, new[] { "Point1" }, DisplayName = "one point")]
    public void BindTheIncludedMembers(int count, string[] expected)
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    [TestProperty("spec", "AC-SCEN-009.1")]
    [DataRow("""{ "expect": { "equals": 7 } }""",
             "expected 7, but was 42",
             DisplayName = "equals")]
    public void NameTheBoundOnFailure(string step, string detail)
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 8 (a nested bracket and a wrapped attribute hide the method) expected 1" }

    # Case 8b: the same two blocks over conforming methods -> 0
    Set-Content -LiteralPath $w -NoNewline -Value @'
public class WrapShould
{
    [TestMethod]
    [TestProperty("spec", "AC-GATE-006.3")]
    [DataRow(1, new[] { "Point1" }, DisplayName = "one point")]
    public void BindIncludedMembers(int count, string[] expected)
    {
        // Arrange / Act
        // Assert
    }

    [TestMethod]
    [TestProperty("spec", "AC-SCEN-009.1")]
    [DataRow("""{ "expect": { "equals": 7 } }""",
             "expected 7, but was 42",
             DisplayName = "equals")]
    public void NameBoundOnFailure(string step, string detail)
    {
        // Act
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 8b (the same blocks over conforming methods) expected 0" }

    # Case 9: an attribute that never closes must not run away into the file below it, and must not
    # cost more than the line it sits on - a block whose `[` has no `]` swallows the whole rest of a
    # file once continuation lines are allowed, and the methods below it go unchecked.
    New-File 'Fake.Sdk.Test/UnclosedShould.cs' @'
public class UnclosedShould
{
    [DataRow("a", "b",

    [TestMethod]
    [TestProperty("spec", "AC-GATE-007.1")]
    public void ReturnTheValue()
    {
        // Act
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 9 (an unclosed attribute above a cited method) expected 1" }
    Remove-Item (Join-Path $tmp 'Fake.Sdk.Test/UnclosedShould.cs')

    # Case 10: C# spells a verbatim string two ways when it is also interpolated - `$@"…"` and
    # `@$"…"`. Both hold a `]` and a backslash that is NOT an escape, so reading either as an
    # ordinary quoted string walks past the closing quote and loses the method.
    # One spelling per file: two hidden methods in one file would let either one carry the case.
    $v = New-File 'Fake.Sdk.Test/VerbatimShould.cs' @'
public class VerbatimShould
{
    [TestMethod]
    [TestProperty("spec", "AC-CLI-001.1")]
    [DataRow(@$"C:\out\[x]\", 1)]
    public void ReturnThePath(string path, int n)
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 10 (@`$ verbatim spelling) expected 1" }
    Set-Content -LiteralPath $v -NoNewline -Value @'
public class VerbatimShould
{
    [TestMethod]
    [TestProperty("spec", "AC-CLI-001.2")]
    [DataRow($@"C:\out\[y]\", 2)]
    public void ReturnThePath(string path, int n)
    {
        Assert.IsTrue(true);
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 10b (`$@ verbatim spelling) expected 1" }
    Remove-Item $v

    # Case 11: the anti-vacuous floor. A gate that can silently match nothing can silently die with
    # nothing else in CI noticing - the shape this gate was in for a week while its block pattern
    # skipped 21 cited methods. Test roots present but not one cited method parsed -> 1; and no
    # *.Test root at all, where an empty root list makes Get-ChildItem enumerate the process cwd -> 1.
    Get-ChildItem -LiteralPath $tmp -Recurse -Filter *.cs | Remove-Item -Force
    if ((Invoke-Lint) -ne 1) { throw "Case 11 (roots present, zero cited tests) expected 1" }
    Get-ChildItem -LiteralPath $tmp -Directory -Filter '*.Test' -Recurse | Remove-Item -Recurse -Force
    if ((Invoke-Lint) -ne 1) { throw "Case 11b (no *.Test root at all) expected 1" }

    Write-Host 'test-style-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
