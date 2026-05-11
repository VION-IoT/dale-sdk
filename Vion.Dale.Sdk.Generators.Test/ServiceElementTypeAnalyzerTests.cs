using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ServiceElementTypeAnalyzerTests
    {
        [TestMethod]
        public async Task SupportedTypes_NoDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public bool BoolProp { get; set; }
    [ServiceProperty] public string StringProp { get; set; }
    [ServiceProperty] public int IntProp { get; set; }
    [ServiceProperty] public long LongProp { get; set; }
    [ServiceProperty] public short ShortProp { get; set; }
    [ServiceProperty] public float FloatProp { get; set; }
    [ServiceProperty] public double DoubleProp { get; set; }
    [ServiceProperty] public DateTime DateTimeProp { get; set; }
    [ServiceProperty] public TimeSpan TimeSpanProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task EnumType_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum MyState { Active, Inactive }

public class MyBlock
{
    [ServiceProperty] public MyState State { get; set; }
    [ServiceMeasuringPoint] public MyState CurrentState { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task UnsupportedType_ServiceProperty_ReportsDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public Guid {|#0:Id|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Id", "ServiceProperty", "Guid");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task UnsupportedType_ServiceMeasuringPoint_ReportsDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public Guid {|#0:Id|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Id", "ServiceMeasuringPoint", "Guid");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task CustomClassType_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class Payload { }

public class MyBlock
{
    [ServiceProperty] public Payload {|#0:Data|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Data", "ServiceProperty", "Payload");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task PropertyWithoutAttribute_NoDiagnostic()
        {
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    public Guid Id { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        // --- Nullable types ---

        [TestMethod]
        public async Task NullableString_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string? NullableStringProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task NullableInt_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int? NullableIntProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        // --- Newly-supported unsigned primitives ---

        [TestMethod]
        public async Task BytePrimitive_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public byte ByteProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task UShortPrimitive_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ushort UShortProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task UIntPrimitive_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public uint UIntProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        // --- ImmutableArray<T> ---

        [TestMethod]
        public async Task ImmutableArrayOfDouble_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<double> Samples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayOfNullableInt_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<int?> NullableSamples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        // --- Flat readonly record struct ---

        [TestMethod]
        public async Task FlatReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public Coordinates Position { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task NullableFlatReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public Coordinates? OptionalPosition { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayOfReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<Coordinates> Track { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayOfNullableReadonlyRecordStruct_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public readonly record struct Coordinates(double Lat, double Lon);

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<Coordinates?> SparseTrack { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        // --- Rejected types ---

        [TestMethod]
        public async Task Decimal_WasAllowedNowRejected_ReportsDiagnostic()
        {
            // decimal was previously in the whitelist; it is removed per spec §5.1.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public decimal {|#0:Amount|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Amount", "ServiceProperty", "decimal");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ListOfDouble_ReportsDiagnostic()
        {
            // Must use ImmutableArray<T>, not List<T>.
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public List<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceProperty", "List<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task RawArray_ReportsDiagnostic()
        {
            // T[] is not supported; only ImmutableArray<T>.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int[] {|#0:Values|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Values", "ServiceProperty", "int[]");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task IEnumerableOfDouble_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public IEnumerable<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceProperty", "IEnumerable<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task IReadOnlyListOfDouble_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public IReadOnlyList<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceProperty", "IReadOnlyList<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ValueTuple_ReportsDiagnostic()
        {
            // (double Lat, double Lon) is a value-tuple, not a readonly record struct.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public (double Lat, double Lon) {|#0:Position|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceProperty", "(double Lat, double Lon)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NonReadonlyRecordStruct_ReportsDiagnostic()
        {
            // record struct without readonly is not accepted.
            var source = @"
using Vion.Dale.Sdk.Core;

public record struct Mutable(double X, double Y);

public class MyBlock
{
    [ServiceProperty] public Mutable {|#0:Position|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Position", "ServiceProperty", "Mutable");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task StructWithNestedStructField_ReportsDiagnostic()
        {
            // Nested structs are not allowed — struct fields must be primitive/enum/string/TimeSpan.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Inner(double Value);
public readonly record struct Outer(Inner Nested);

public class MyBlock
{
    [ServiceProperty] public Outer {|#0:Data|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Data", "ServiceProperty", "Outer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task StructWithDecimalField_ReportsDiagnostic()
        {
            // decimal is not a supported primitive, even inside a struct.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Price(decimal Amount);

public class MyBlock
{
    [ServiceProperty] public Price {|#0:Cost|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Cost", "ServiceProperty", "Price");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ULong_ReportsDiagnostic()
        {
            // ulong is deferred — not in the supported set.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ulong {|#0:Counter|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Counter", "ServiceProperty", "ulong");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task SByte_ReportsDiagnostic()
        {
            // sbyte is deferred — not in the supported set.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public sbyte {|#0:Offset|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType).WithLocation(0).WithArguments("Offset", "ServiceProperty", "sbyte");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }
    }
}
