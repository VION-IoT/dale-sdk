using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MinChangeUnparseableAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task ValidDoubleMinChange_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task ValidIntMinChange_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""5"")] public int Count { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task ValidTimeSpanMinChange_NoDiagnostic()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task NonNumericDoubleMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""abc"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "abc", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task DecimalPointOnIntMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""1.5"")|}] public int Count { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Count", "1.5", "int", "An invariant-culture integer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task BadUnitTimeSpanMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceMeasuringPoint(MinChange = ""5x"")|}] public TimeSpan Latency { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Latency", "5x", "System.TimeSpan", "A duration (number with optional us/ms/s/m/h suffix)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task UnparseableOnCustomType_NoDiagnostic()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task ValidMinChangeOnNullableDouble_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.25"")] public double? Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task ValidDecimalMinChange_NoDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.01"")] public decimal Price { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task NonNumericDecimalMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""cheap"")|}] public decimal Price { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Price", "cheap", "decimal", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task NegativeDoubleMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""-0.5"")|}] public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "-0.5", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task NegativeIntMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""-3"")|}] public int Count { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Count", "-3", "int", "An invariant-culture integer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.2")]
        public async Task NegativeTimeSpanMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""-1s"")|}] public TimeSpan Uptime { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Uptime", "-1s", "System.TimeSpan", "A duration (number with optional us/ms/s/m/h suffix)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedMeasuringPointBadMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.5"")]
    [{|#0:ServiceMeasuringPoint(MinChange = ""loads"")|}]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "loads", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedServicePropertyBadMinChange_ReportsDiagnostic()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""loads"")|}]
    [ServiceMeasuringPoint(MinChange = ""0.5"")]
    public double Voltage { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "loads", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-012.7")]
        public async Task DualAnnotatedBothMinChangesBad_ReportsOnEachAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinChange = ""loads"")|}]
    [{|#1:ServiceMeasuringPoint(MinChange = ""heaps"")|}]
    public double Voltage { get; set; }
}";
            var onProperty = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                             .WithLocation(0)
                                             .WithArguments("Voltage", "loads", "double", "An invariant-culture number");
            var onMeasuringPoint = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE035_MinChangeUnparseable)
                                                   .WithLocation(1)
                                                   .WithArguments("Voltage", "heaps", "double", "An invariant-culture number");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinChangeUnparseableAnalyzer>(source, onProperty, onMeasuringPoint);
        }
    }
}