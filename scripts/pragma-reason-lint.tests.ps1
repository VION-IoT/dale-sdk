#requires -Version 7
# Self-test for pragma-reason-lint.ps1 (a DALE suppression says why). Plain pwsh, NOT Pester.
# Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/pragma-reason-lint.tests.ps1`.
#
# Cases assert the MESSAGE as well as the exit code where the gate distinguishes two defects. Both
# report exit 1, so an exit-code-only case cannot tell "nothing was written" from "something was
# written that says nothing" - and a mutation collapsing the two survived a run that only read the
# code.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'pragma-reason-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("pragmareason-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, $content) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    Set-Content -LiteralPath $p -Value $content -NoNewline
    return $p
}
$script:Output = ''
function Invoke-Lint {
    $script:Output = (pwsh -NoProfile -File $lint -RepoRoot $tmp) -join "`n"
    return $LASTEXITCODE
}
function Assert-Says($fragment, $case) {
    if ($script:Output -notmatch [regex]::Escape($fragment)) {
        throw "$case expected the report to say '$fragment'; it said:`n$script:Output"
    }
}

try {
    # A site that always conforms, so every case below is judged on its own file rather than on the
    # anti-vacuous floor (case 12), which fires only when the whole scan finds nothing.
    New-File 'Fake.Sdk.Test/Anchor.cs' @'
public class Anchor
{
    // The endpoint below carries no service member, so DALE045 warns that it emits no relation half;
    // the fixture is about the identifier it derives, not about the halves.
#pragma warning disable DALE045
    public Endpoint Point { get; } = new();
#pragma warning restore DALE045
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 0 (the anchor alone) expected 0" }

    # Case 1: the reason after the directive on the same line -> 0
    $one = New-File 'Fake.Sdk.Test/Trailing.cs' @'
public class Trailing
{
#pragma warning disable DALE045 // The fixture is about the identifier, not the relation half.
    public Endpoint Point { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (trailing reason) expected 0" }
    Remove-Item $one

    # Case 2: no reason on the line and nothing above it -> 1
    $bare = New-File 'Fake.Sdk.Test/Bare.cs' @'
public class Bare
{
#pragma warning disable DALE045
    public Endpoint Point { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (no reason at all) expected 1" }
    Assert-Says 'suppressed with no reason' 'Case 2'
    Remove-Item $bare

    # Case 3: the shape this gate was written for, copied from the tree it landed on -
    # Vion.Dale.Sdk.TestKit.Test/EmissionPolicyShould.cs before this gate, where the reason sat above
    # the CLASS and the directive below the brace inherited nothing -> 1
    $real = New-File 'Fake.Sdk.Test/EmissionPolicyShould.cs' @'
public class EmissionPolicyShould
{
    // Each of the three below is rejected by DALE035 at compile time; suppressed here to reach the
    // start-time backstop, which is what a member gets when the compile-time gate was bypassed.
    private sealed class UnreadableDeadbandBlock : LogicBlockBase
    {
#pragma warning disable DALE035
        [ServiceProperty(MinChange = "loads")]
        public double Voltage { get; set; }
#pragma warning restore DALE035
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 3 (the reason above the class, not the directive) expected 1" }
    Assert-Says 'suppressed with no reason' 'Case 3'

    # Case 3b: the same file once the directive carries the shape it exempts -> 0
    Set-Content -LiteralPath $real -NoNewline -Value @'
public class EmissionPolicyShould
{
    // Each of the three below is rejected by DALE035 at compile time; suppressed here to reach the
    // start-time backstop, which is what a member gets when the compile-time gate was bypassed.
    private sealed class UnreadableDeadbandBlock : LogicBlockBase
    {
#pragma warning disable DALE035 // MinChange = "loads" does not read as a number.
        [ServiceProperty(MinChange = "loads")]
        public double Voltage { get; set; }
#pragma warning restore DALE035
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 3b (the reason on the directive) expected 0" }
    Remove-Item $real

    # Case 4: a comment built only from the directive's own words says nothing the line does not.
    # The words it is built from are short or the directive's; dropping either rule lets it through.
    $vacuous = New-File 'Fake.Sdk.Test/Vacuous.cs' @'
public class Vacuous
{
    // Suppress the DALE045 analyzer warning here.
#pragma warning disable DALE045
    public Endpoint Point { get; } = new();

#pragma warning disable DALE024 // DALE024 is disabled.
    public int Count { get; set; }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 4 (only the directive's own words) expected 1" }
    Assert-Says "built only from the directive's own words" 'Case 4'

    # Case 4b: the floor is ONE word the directive does not say, not a prose-quality bar - the
    # citation form comment-conventions blesses is two words long and has to pass.
    Set-Content -LiteralPath $vacuous -NoNewline -Value @'
public class Vacuous
{
#pragma warning disable DALE045 // Bug in the tool; see VION-133.
    public Endpoint Point { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 4b (a short citation reason) expected 0" }
    Remove-Item $vacuous

    # Case 5: a blank line, and then any code, ends the run above - a reason two members up is not
    # this directive's -> 1
    $detached = New-File 'Fake.Sdk.Test/Detached.cs' @'
public class Detached
{
    // The endpoint below carries no service member, so the relation half is deliberately absent.

#pragma warning disable DALE045
    public Endpoint Point { get; } = new();
#pragma warning restore DALE045

    // The endpoint below carries no service member, so the relation half is deliberately absent.
    public Endpoint Other { get; } = new();
#pragma warning disable DALE045
    public Endpoint Third { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 5 (a blank line and a code line end the run) expected 1" }
    Assert-Says 'Detached.cs:5' 'Case 5'
    Assert-Says 'Detached.cs:11' 'Case 5'
    Remove-Item $detached

    # Case 6: a doc comment above documents the member, so it counts only where it names what is
    # being disabled - the shape Vion.Dale.Sdk.Test/TestHelpers/GatingBlocks.cs carries -> 0
    $doc = New-File 'Fake.Sdk.Test/Documented.cs' @'
public class Documented
{
    /// <summary>
    ///     A parameter an author also marked <c>[Persistent]</c> - the combination DALE044 refuses,
    ///     declared here so the suite can reach what persistence does with one that shipped anyway.
    /// </summary>
#pragma warning disable DALE044
    public sealed class PersistedParameterBlock
    {
    }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 6 (a doc comment naming the id) expected 0" }

    # Case 6b: the same doc comment with the id taken out - prose about the member, silent about the
    # suppression, which is what every documented member would otherwise pass on -> 1
    Set-Content -LiteralPath $doc -NoNewline -Value @'
public class Documented
{
    /// <summary>
    ///     A parameter an author also marked <c>[Persistent]</c>, declared here so the suite can
    ///     reach what persistence does with one that shipped anyway.
    /// </summary>
#pragma warning disable DALE044
    public sealed class PersistedParameterBlock
    {
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 6b (a doc comment silent about the id) expected 1" }
    Assert-Says 'suppressed with no reason' 'Case 6b'

    # Case 6c: a doc comment that names the id and nothing else. The XML tags are markup, not words,
    # so this is the same non-reason as case 4 wearing three slashes.
    Set-Content -LiteralPath $doc -NoNewline -Value @'
public class Documented
{
    /// <summary>DALE044.</summary>
#pragma warning disable DALE044
    public sealed class PersistedParameterBlock
    {
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 6c (a doc comment that is only the id) expected 1" }
    Assert-Says "built only from the directive's own words" 'Case 6c'
    Remove-Item $doc

    # Case 7: both ids of a two-id directive are reported together, and one reason covers both -> 1
    $multi = New-File 'Fake.Sdk.Test/Multi.cs' @'
public class Multi
{
#pragma warning disable DALE003, DALE016
    public Reading Value { get; set; }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (a bare two-id directive) expected 1" }
    Assert-Says "'DALE003, DALE016'" 'Case 7'

    # Case 7b: the same pair written as a stack. Another `#pragma warning` line between the comment
    # and the directive is read through, so one reason serves both.
    Set-Content -LiteralPath $multi -NoNewline -Value @'
public class Multi
{
    // DALE003 and DALE016 refuse a struct with no positional constructor at compile time; the
    // fixture pins what the pack path does behind both.
#pragma warning disable DALE003
#pragma warning disable DALE016
    public Reading Value { get; set; }
#pragma warning restore DALE016
#pragma warning restore DALE003
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 7b (a stack of directives under one reason) expected 0" }
    Remove-Item $multi

    # Case 8: not sites - a restore, and two spellings of the directive quoted rather than issued.
    # C# allows nothing but whitespace before a `#pragma`, which is what keeps a quoted one out;
    # drop that anchor and the const below becomes a bare site. A CS suppression is not a site
    # either, and its reason naming a DALE id must not make it one - ids are read from the
    # directive, never from the comment after it. -> 0
    $notsites = New-File 'Fake.Sdk/NotSites.cs' @'
public class NotSites
{
#pragma warning restore DALE045
#pragma warning disable CS8618 // Obsolete: the replacement is gated behind DALE047 until it ships.
#pragma warning restore CS8618

    private const string Hint = "#pragma warning disable DALE026";

    /// <summary>
    ///     Use <c>#pragma warning disable DALE026</c> for one-off custom keys without a constant.
    /// </summary>
    public string Key { get; set; }
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 8 (restore, CS, and two quoted directives) expected 0" }
    # The exit code alone cannot see this: every non-site here would PASS if it were counted as one,
    # because each carries prose. The count is the oracle - only the anchor is a site.
    Assert-Says 'OK - 1 DALE suppression(s)' 'Case 8'
    Remove-Item $notsites

    # Case 9: a directive naming no id at all suppresses every diagnostic, these included, so it is
    # a site and owes the same reason -> 1
    $blanket = New-File 'Fake.Sdk.Test/Blanket.cs' @'
public class Blanket
{
#pragma warning disable
    public Endpoint Point { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 9 (a directive naming no id) expected 1" }
    Assert-Says "'every diagnostic'" 'Case 9'
    Remove-Item $blanket

    # Case 10: a /* */ block above the directive is a reason - comment-conventions names that form
    # first for anything longer than a line -> 0
    $block = New-File 'Fake.Sdk.Test/Block.cs' @'
public class Block
{
    /* The endpoint below carries no service member, so the relation half is deliberately
       absent; the fixture is about the identifier it derives. */
#pragma warning disable DALE045
    public Endpoint Point { get; } = new();
}
'@
    if ((Invoke-Lint) -ne 0) { throw "Case 10 (a block-comment reason) expected 0" }
    Remove-Item $block

    # Case 11: build output is skipped - a bare suppression under obj/ does not fail -> 0
    New-File 'Fake.Sdk/obj/Debug/Generated.cs' @'
public class Generated
{
#pragma warning disable DALE045
    public Endpoint Point { get; } = new();
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 11 (obj/ skipped) expected 0" }

    # Case 12: the anti-vacuous floor. A scan that matches nothing reports what a scan that is
    # silently broken reports, and this gate's own subject is a directive that stops being written.
    Remove-Item (Join-Path $tmp 'Fake.Sdk.Test/Anchor.cs')
    if ((Invoke-Lint) -ne 1) { throw "Case 12 (C# present, zero DALE suppressions) expected 1" }
    Assert-Says 'anti-vacuous floor' 'Case 12'
    Remove-Item -Recurse -Force (Join-Path $tmp 'Fake.Sdk')
    if ((Invoke-Lint) -ne 1) { throw "Case 12b (no C# at all) expected 1" }
    Assert-Says 'anti-vacuous floor' 'Case 12b'

    Write-Host 'pragma-reason-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
