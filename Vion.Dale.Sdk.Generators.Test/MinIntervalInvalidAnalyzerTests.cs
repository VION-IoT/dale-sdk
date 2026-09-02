using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MinIntervalInvalidAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DefaultMinInterval_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task ValidMinInterval_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task ZeroSentinel_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task ZeroMsSentinel_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0ms"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.4")]
        public async Task ExactlyOneMs_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1ms"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task Unparseable_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""soon"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "soon");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task BadUnit_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""5x"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "5x");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task Negative_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""-1s"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "-1s");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.4")]
        public async Task BelowFloorMicroseconds_ReportsWarning()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""500us"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE037_MinIntervalBelowFloor).WithLocation(0).WithArguments("Voltage", "500us");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.4")]
        public async Task BelowFloorFractionalMs_ReportsWarning()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""0.5ms"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE037_MinIntervalBelowFloor).WithLocation(0).WithArguments("Voltage", "0.5ms");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task MeasuringPointBadInterval_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceMeasuringPoint(MinInterval = ""nope"")|}] public double Voltage { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "nope");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedMeasuringPointBadInterval_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"")]
    [{|#0:ServiceMeasuringPoint(MinInterval = ""soon"")|}]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "soon");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.3")]
        public async Task TooLargeToRepresent_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""999999999999999999999h"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "999999999999999999999h");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedServicePropertyBadInterval_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""soon"")|}]
    [ServiceMeasuringPoint(MinInterval = ""1s"")]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "soon");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedBothIntervalsBad_ReportsOnEachAttribute()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""soon"")|}]
    [{|#1:ServiceMeasuringPoint(MinInterval = ""later"")|}]
    public double Voltage { get; set; }
}";
            var onProperty = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "soon");
            var onMeasuringPoint = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(1).WithArguments("Voltage", "later");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, onProperty, onMeasuringPoint);
        }
    }
}