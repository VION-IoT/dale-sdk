using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StructServiceElementAnalyzerTests
    {
        // --- Types that should trigger DALE016 ---

        [TestMethod]
        public async Task RegularStruct_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public struct Coordinates { public double Lat; public double Lon; }

public class MyBlock
{
    [ServiceProperty] public Coordinates {|#0:Position|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceProperty", "Coordinates");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MutableRecordStruct_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public Coordinates {|#0:Position|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceProperty", "Coordinates");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ReadonlyRecordStructWithNonFlatField_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Inner(double Value);
public readonly record struct Outer(Inner Nested);

public class MyBlock
{
    [ServiceProperty] public Outer {|#0:Data|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord).WithLocation(0).WithArguments("Data", "ServiceProperty", "Outer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MeasuringPoint_RegularStruct_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public struct Coordinates { public double Lat; public double Lon; }

public class MyBlock
{
    [ServiceMeasuringPoint] public Coordinates {|#0:Position|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceMeasuringPoint", "Coordinates");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NullableRegularStruct_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public struct Coordinates { public double Lat; public double Lon; }

public class MyBlock
{
    [ServiceProperty] public Coordinates? {|#0:Position|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceProperty", "Coordinates");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ImmutableArrayOfRegularStruct_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public struct Coordinates { public double Lat; public double Lon; }

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<Coordinates> {|#0:Track|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord)
                                           .WithLocation(0)
                                           .WithArguments("Track", "ServiceProperty", "Coordinates");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source, expected);
        }

        // --- Types that should NOT trigger DALE016 ---

        [TestMethod]
        public async Task ValidFlatReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public Coordinates Position { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task NullableValidReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public Coordinates? Position { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayOfValidReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<Coordinates> Track { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task PrimitiveProp_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int Value { get; set; }
    [ServiceProperty] public double Rate { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task SystemValueTypes_NoDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public DateTime Timestamp { get; set; }
    [ServiceProperty] public TimeSpan Duration { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task EnumProp_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum MyState { Active, Inactive }

public class MyBlock
{
    [ServiceProperty] public MyState State { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task PropertyWithoutAttribute_RegularStruct_NoDiagnostic()
        {
            var source = @"
public struct Coordinates { public double Lat; public double Lon; }

public class MyBlock
{
    public Coordinates Position { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }
    }
}