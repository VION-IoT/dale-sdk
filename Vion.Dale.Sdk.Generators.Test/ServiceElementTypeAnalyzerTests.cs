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
    [ServiceProperty] public decimal DecimalProp { get; set; }
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
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                .WithLocation(0)
                .WithArguments("Id", "ServiceProperty", "Guid");
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
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                .WithLocation(0)
                .WithArguments("Id", "ServiceMeasuringPoint", "Guid");
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
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                .WithLocation(0)
                .WithArguments("Data", "ServiceProperty", "Payload");
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
    }
}
