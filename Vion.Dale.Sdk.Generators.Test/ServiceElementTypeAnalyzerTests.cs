using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ServiceElementTypeAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptEverySupportedScalarType()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptEnumType()
        {
            // Arrange / Act / Assert
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

        // Guid is a supported service-element type (maps to format:uuid) as of the string-formats
        // change. DALE003's "unsupported type" coverage lives in the decimal / array / class tests below.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptGuidOnServiceProperty()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public Guid Id { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptGuidOnMeasuringPoint()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public Guid Id { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectCustomClassType()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task StaySilentOnPropertyWithoutServiceAttribute()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptNullableString()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string? NullableStringProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptNullableInt()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptBytePrimitive()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public byte ByteProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptUnsignedShortPrimitive()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ushort UShortProp { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptUnsignedIntPrimitive()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptImmutableArrayOfDouble()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptImmutableArrayOfNullableInt()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptFlatReadonlyRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptNullableFlatReadonlyRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptImmutableArrayOfRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task AcceptImmutableArrayOfNullableRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectDecimal()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectMutableList()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectRawArray()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectEnumerableInterface()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectReadOnlyListInterface()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectValueTuple()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectNonReadonlyRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectStructWithNestedStructField()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectStructWithDecimalField()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectUnsignedLong()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.1")]
        public async Task RejectSignedByte()
        {
            // Arrange / Act / Assert
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