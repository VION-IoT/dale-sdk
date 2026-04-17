using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StatusIndicatorAnalyzerTests
    {
        [TestMethod]
        public async Task StatusIndicatorOnEnum_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum ConnectionState { Connected, Disconnected }

public class MyBlock
{
    [StatusIndicator] public ConnectionState State { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorAnalyzer>(source);
        }

        [TestMethod]
        public async Task StatusIndicatorOnString_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [StatusIndicator] public string {|#0:Status|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum).WithLocation(0).WithArguments("Status", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task StatusIndicatorOnInt_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [StatusIndicator] public int {|#0:Code|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum).WithLocation(0).WithArguments("Code", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NoStatusIndicator_NoDiagnostic()
        {
            var source = @"
public class MyBlock
{
    public string Status { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StatusIndicatorAnalyzer>(source);
        }
    }
}