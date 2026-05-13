using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StatusIndicatorRequiresEnumAnalyzerTests
    {
        [TestMethod]
        public async Task StatusIndicatorOnEnum_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum AlarmState { Ok, Warning, Critical }

public class MyBlock
{
    [Presentation(StatusIndicator = true)] public AlarmState State { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorRequiresEnumAnalyzer>(source);
        }

        [TestMethod]
        public async Task StatusIndicatorOnNullableEnum_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum AlarmState { Ok, Warning, Critical }

public class MyBlock
{
    [Presentation(StatusIndicator = true)] public AlarmState? State { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorRequiresEnumAnalyzer>(source);
        }

        [TestMethod]
        public async Task StatusIndicatorOnNonEnum_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(StatusIndicator = true)] public int {|#0:Counter|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE024_StatusIndicatorRequiresEnum).WithLocation(0).WithArguments("Counter", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorRequiresEnumAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task StatusIndicatorFalse_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorRequiresEnumAnalyzer>(source);
        }
    }
}
