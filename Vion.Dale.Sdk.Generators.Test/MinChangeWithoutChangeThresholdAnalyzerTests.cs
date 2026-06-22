using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MinChangeWithoutChangeThresholdAnalyzerTests
    {
        [TestMethod]
        public async Task MinChangeOnDouble_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source);
        }

        [TestMethod]
        public async Task MinChangeOnTimeSpan_NoDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint(MinChange = ""1s"")] public TimeSpan Uptime { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source);
        }

        [TestMethod]
        public async Task MinChangeUnset_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public bool Fault { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source);
        }

        [TestMethod]
        public async Task MinChangeOnBool_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""1"")] public bool {|#0:Fault|} { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold).WithLocation(0).WithArguments("Fault", "bool");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MinChangeOnStringNoThreshold_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""x"")] public string {|#0:Status|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold).WithLocation(0).WithArguments("Status", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MinChangeOnCustomStructWithRegisteredThreshold_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

public readonly record struct ThreePhaseCurrent(double A, double B, double C);

public sealed class ThreePhaseCurrentChangeThreshold : IChangeThreshold<ThreePhaseCurrent>
{
    public bool Exceeds(in ThreePhaseCurrent last, in ThreePhaseCurrent now, string threshold) => false;
}

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.5"")] public ThreePhaseCurrent Current { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source);
        }

        [TestMethod]
        public async Task MinChangeOnCustomStructWithoutThreshold_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Position(double X, double Y);

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.5"")] public Position {|#0:Where|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold).WithLocation(0).WithArguments("Where", "Position");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MinChangeOnNullableDouble_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double? Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeWithoutChangeThresholdAnalyzer>(source);
        }
    }
}