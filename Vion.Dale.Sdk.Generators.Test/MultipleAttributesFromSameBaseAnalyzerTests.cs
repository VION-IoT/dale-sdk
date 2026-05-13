using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MultipleAttributesFromSameBaseAnalyzerTests
    {
        [TestMethod]
        public async Task SingleServiceProperty_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Unit = ""kW"")] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultipleAttributesFromSameBaseAnalyzer>(source);
        }

        [TestMethod]
        public async Task ServicePropertyPlusMeasuringPoint_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty]
    [ServiceMeasuringPoint]
    public int TotalBlinks { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultipleAttributesFromSameBaseAnalyzer>(source);
        }

        [TestMethod]
        public async Task TwoSubclassesOfServiceProperty_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class Kilowatts : ServicePropertyAttribute { public Kilowatts() { Unit = ""kW""; } }
public class Volts : ServicePropertyAttribute { public Volts() { Unit = ""V""; } }

public class MyBlock
{
    [{|#0:Kilowatts|}]
    [{|#1:Volts|}]
    public double Power { get; set; }
}";
            var d0 = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE019_MultipleAttributesFromSameBase)
                                     .WithLocation(0)
                                     .WithArguments("Power", "ServicePropertyAttribute", "[Kilowatts], [Volts]");
            var d1 = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE019_MultipleAttributesFromSameBase)
                                     .WithLocation(1)
                                     .WithArguments("Power", "ServicePropertyAttribute", "[Kilowatts], [Volts]");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultipleAttributesFromSameBaseAnalyzer>(source, d0, d1);
        }
    }
}
