using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class TimerMethodAnalyzerTests
    {
        // --- DALE002: Timer method must be void and parameterless ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.1")]
        public async Task StaySilentOnVoidParameterlessTimer()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(10.0)]
    private void Tick() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.1")]
        public async Task ReportTimerWithReturnType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(10.0)]
    private int {|#0:Tick|}() { return 0; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE002_TimerMethodSignature).WithLocation(0).WithArguments("Tick", "returns int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.1")]
        public async Task ReportTimerWithParameters()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(10.0)]
    private void {|#0:Tick|}(int x) { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE002_TimerMethodSignature).WithLocation(0).WithArguments("Tick", "has 1 parameter(s)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.1")]
        public async Task ReportTimerWithReturnTypeAndParameters()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(10.0)]
    private int {|#0:Tick|}(int x, string y) { return 0; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE002_TimerMethodSignature).WithLocation(0).WithArguments("Tick", "returns int and has 2 parameter(s)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        // --- DALE005: Timer interval must be > 0 ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.2")]
        public async Task StaySilentOnPositiveInterval()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(5.0)]
    private void Tick() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.2")]
        public async Task ReportZeroInterval()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(0.0)]
    private void {|#0:Tick|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE005_TimerIntervalMustBePositive).WithLocation(0).WithArguments("Tick", "0");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.2")]
        public async Task ReportNegativeInterval()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(-1.0)]
    private void {|#0:Tick|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE005_TimerIntervalMustBePositive).WithLocation(0).WithArguments("Tick", "-1");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        // --- DALE012: Duplicate timer identifiers ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task StaySilentOnDistinctIdentifiers()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(5.0)]
    private void Tick() { }

    [Timer(10.0)]
    private void Tock() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task ReportDuplicateExplicitIdentifier()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(5.0, ""MyTimer"")]
    private void Tick() { }

    [Timer(10.0, ""MyTimer"")]
    private void {|#0:Tock|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE012_DuplicateTimerIdentifier).WithLocation(0).WithArguments("Tick", "Tock", "MyTimer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task ReportExplicitIdentifierMatchingMethodName()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(5.0)]
    private void Tick() { }

    [Timer(10.0, ""Tick"")]
    private void {|#0:OtherMethod|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE012_DuplicateTimerIdentifier).WithLocation(0).WithArguments("Tick", "OtherMethod", "Tick");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.2")]
        [DataRow("0.0 / 0.0", "NaN", DisplayName = "not a number")]
        [DataRow("1.0 / 0.0", "\u221E", DisplayName = "positive infinity")]
        public async Task ReportNonFiniteTimerInterval(string interval, string rendered)
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Timer(" + interval + @")]
    public void {|#0:Tick|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE005_TimerIntervalMustBePositive).WithLocation(0).WithArguments("Tick", rendered);
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task ReportTimerIdentifierSharedBetweenBaseAndDerivedMethod()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class BlockBase
{
    [Timer(60.0, ""poll"")]
    protected void PollSlowly() { }
}

public class MyBlock : BlockBase
{
    [Timer(5.0, ""poll"")]
    private void {|#0:PollQuickly|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE012_DuplicateTimerIdentifier).WithLocation(0).WithArguments("PollSlowly", "PollQuickly", "poll");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task StaySilentWhenOverrideSharesItsVirtualTimerIdentifier()
        {
            // Arrange / Act / Assert
            // An override and the virtual it overrides are one timer to the binder, so the shared
            // identifier is not a collision.
            var source = @"
using Vion.Dale.Sdk.Core;

public class BlockBase
{
    [Timer(60.0, ""virtual"")]
    protected virtual void Tick() { }
}

public class MyBlock : BlockBase
{
    [Timer(5.0, ""virtual"")]
    protected override void Tick() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-007.3")]
        public async Task ReportTimerIdentifierSharedWithNewShadowedMethod()
        {
            // Arrange / Act / Assert
            // A `new` declaration is a second method, not one timer, so both reach the callback map.
            var source = @"
using Vion.Dale.Sdk.Core;

public class BlockBase
{
    [Timer(60.0, ""shadow"")]
    public void Tick() { }
}

public class MyBlock : BlockBase
{
    [Timer(5.0, ""shadow"")]
    public new void {|#0:Tick|}() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE012_DuplicateTimerIdentifier).WithLocation(0).WithArguments("Tick", "Tick", "shadow");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TimerMethodAnalyzer>(source, expected);
        }
    }
}