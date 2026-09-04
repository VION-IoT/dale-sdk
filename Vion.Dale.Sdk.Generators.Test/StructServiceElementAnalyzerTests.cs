using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StructServiceElementAnalyzerTests
    {
        // Guid is a struct but a recognized built-in (maps to schema format:uuid) — not a user struct
        // that must be a flat record struct. Regression guard for the DALE016 Guid exemption.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnGuid()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public Guid Id { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        // --- Types that should trigger DALE016 ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportPlainStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportMutableRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportRecordStructWithNonFlatField()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportPlainStructOnMeasuringPoint()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportNullablePlainStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task ReportImmutableArrayOfPlainStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnFlatReadonlyRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnNullableFlatRecordStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnImmutableArrayOfFlatRecordStruct()
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
            await AnalyzerTestBase.VerifyAnalyzerAsync<StructServiceElementAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnPrimitiveProperty()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnSystemValueTypes()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnEnumProperty()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.4")]
        public async Task StaySilentOnStructWithoutServiceAttribute()
        {
            // Arrange / Act / Assert
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