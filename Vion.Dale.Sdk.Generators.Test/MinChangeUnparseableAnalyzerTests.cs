using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MinChangeUnparseableAnalyzerTests
    {
        [TestMethod]
        public async Task ValidDoubleMinChange_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task ValidIntMinChange_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""5"")] public int Count { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task ValidTimeSpanMinChange_NoDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint(MinChange = ""250ms"")] public TimeSpan Latency { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task NonNumericDoubleMinChange_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""abc"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "abc", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task DecimalPointOnIntMinChange_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""1.5"")] public int {|#0:Count|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Count", "1.5", "int", "An invariant-culture integer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task BadUnitTimeSpanMinChange_ReportsDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint(MinChange = ""5x"")] public TimeSpan {|#0:Latency|} { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Latency", "5x", "System.TimeSpan", "A duration (number with optional us/ms/s/m/h suffix)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task UnparseableOnCustomType_NoDiagnostic()
        {
            // Custom-threshold types have an opaque MinChange format; never parse-checked.
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
    [ServiceProperty(MinChange = ""whatever-the-type-wants"")] public ThreePhaseCurrent Current { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task ValidMinChangeOnNullableDouble_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.25"")] public double? Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task ValidDecimalMinChange_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.01"")] public decimal Price { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        public async Task NonNumericDecimalMinChange_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""cheap"")] public decimal {|#0:Price|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Price", "cheap", "decimal", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }
    }
}
