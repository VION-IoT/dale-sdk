using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class DeadbandWithoutThrottleAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task MinChangeWithDefaultInterval_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task MinChangeWithRealInterval_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"", MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task ZeroIntervalWithoutMinChange_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task ZeroIntervalWithMinChange_ReportsInfo()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""0"", MinChange = ""0.1"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task ZeroMsIntervalWithMinChange_ReportsInfo()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceMeasuringPoint(MinInterval = ""0ms"", MinChange = ""0.1"")|}] public double Voltage { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0ms");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.6")]
        public async Task ZeroIntervalMinChangeButImmediate_NoDiagnostic()
        {
            // Arrange / Act / Assert
            // Immediate bypasses the deadband too, so "deadband only" doesn't apply — leave it to DALE038.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"", MinChange = ""0.1"", Immediate = true)] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedMeasuringPointZeroIntervalWithMinChange_ReportsInfo()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"")]
    [{|#0:ServiceMeasuringPoint(MinInterval = ""0"", MinChange = ""0.5"")|}]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedServicePropertyZeroIntervalWithMinChange_ReportsInfo()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""0"", MinChange = ""0.5"")|}]
    [ServiceMeasuringPoint(MinInterval = ""1s"")]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedBothDeadbandOnly_ReportsOnEachAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""0"", MinChange = ""0.5"")|}]
    [{|#1:ServiceMeasuringPoint(MinInterval = ""0ms"", MinChange = ""1.5"")|}]
    public double Voltage { get; set; }
}";
            var onProperty = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(0).WithArguments("Voltage", "0");
            var onMeasuringPoint = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE039_DeadbandWithoutThrottle).WithLocation(1).WithArguments("Voltage", "0ms");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DeadbandWithoutThrottleAnalyzer>(source, onProperty, onMeasuringPoint);
        }
    }
}