using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MinIntervalInvalidAnalyzerTests
    {
        [TestMethod]
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
        public async Task Unparseable_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""soon"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "soon");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task BadUnit_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""5x"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "5x");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task BelowFloorMicroseconds_ReportsWarning()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""500us"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE037_MinIntervalBelowFloor).WithLocation(0).WithArguments("Voltage", "500us");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task BelowFloorFractionalMs_ReportsWarning()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""0.5ms"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE037_MinIntervalBelowFloor).WithLocation(0).WithArguments("Voltage", "0.5ms");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MeasuringPointBadInterval_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint(MinInterval = ""nope"")] public double {|#0:Voltage|} { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Voltage", "nope");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }
    }
}
