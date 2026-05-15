using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MeasuringPointAnalyzerTests
    {
        [TestMethod]
        public async Task MeasuringPointWithPrivateSetter_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        public async Task MeasuringPointGetOnly_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        public async Task MeasuringPointWithPublicSetter_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double {|#0:Value|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE004_MeasuringPointPublicSetter).WithLocation(0).WithArguments("Value");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MeasuringPointAndServicePropertyWithPublicSetter_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty]
    [ServiceMeasuringPoint]
    public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        public async Task MeasuringPointWithInternalSetter_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; internal set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }
    }
}