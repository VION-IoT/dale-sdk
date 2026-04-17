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
        public async Task VoidParameterlessTimer_NoDiagnostic()
        {
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
        public async Task TimerWithReturnType_ReportsDiagnostic()
        {
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
        public async Task TimerWithParameters_ReportsDiagnostic()
        {
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
        public async Task TimerWithReturnTypeAndParameters_ReportsDiagnostic()
        {
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
        public async Task TimerWithPositiveInterval_NoDiagnostic()
        {
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
        public async Task TimerWithZeroInterval_ReportsDiagnostic()
        {
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
        public async Task TimerWithNegativeInterval_ReportsDiagnostic()
        {
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
        public async Task UniqueTimerIdentifiers_NoDiagnostic()
        {
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
        public async Task DuplicateExplicitIdentifier_ReportsDiagnostic()
        {
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
        public async Task ExplicitIdentifierMatchesOtherMethodName_ReportsDiagnostic()
        {
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
    }
}
