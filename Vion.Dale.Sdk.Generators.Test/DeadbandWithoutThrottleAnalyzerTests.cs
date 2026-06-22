using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class DeadbandWithoutThrottleAnalyzerTests
    {
        [TestMethod]
        public async Task MinChangeWithDefaultInterval_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        public async Task MinChangeWithRealInterval_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"", MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        public async Task ZeroIntervalWithoutMinChange_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        public async Task ZeroIntervalWithMinChange_ReportsInfo()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"", MinChange = ""0.1"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ZeroMsIntervalWithMinChange_ReportsInfo()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint(MinInterval = ""0ms"", MinChange = ""0.1"")] public double {|#0:Voltage|} { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0ms");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ZeroIntervalMinChangeButImmediate_NoDiagnostic()
        {
            // Immediate bypasses the deadband too, so "deadband only" doesn't apply — leave it to DALE038.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"", MinChange = ""0.1"", Immediate = true)] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }
    }
}