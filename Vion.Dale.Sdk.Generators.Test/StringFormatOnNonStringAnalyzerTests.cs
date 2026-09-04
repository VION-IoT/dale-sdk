using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StringFormatOnNonStringAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.1")]
        public async Task StaySilentOnStringFormatOverString()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(StringFormat = ""ipv4"")] public string Ip { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.1")]
        public async Task StaySilentWhenStringFormatUnset()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string Notes { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.1")]
        public async Task ReportStringFormatOnNonString()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(StringFormat = ""ipv4"")|}] public int Port { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Port", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.1")]
        public async Task ReportReservedTypeKindFormatOnString()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(StringFormat = ""uuid"")|}] public string Id { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Id", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.2")]
        public async Task ReportMisplacedStringFormatOnMeasuringPointOfDualAnnotatedMember()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty]
    [{|#0:ServiceMeasuringPoint(StringFormat = ""ipv4"")|}]
    public int Rssi { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Rssi", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.2")]
        public async Task ReportMisplacedStringFormatOnEachAttributeOfDualAnnotatedMember()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(StringFormat = ""ipv4"")|}]
    [{|#1:ServiceMeasuringPoint(StringFormat = ""ipv4"")|}]
    public int Rssi { get; set; }
}";
            var first = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Rssi", "int");
            var second = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(1).WithArguments("Rssi", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, first, second);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.3")]
        public async Task ReportMisplacedStringFormatOnStructField()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Link(
    [{|#0:StructField(StringFormat = ""ipv4"")|}] int Port);";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Port", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.3")]
        public async Task StaySilentOnStringFormatOnStringStructField()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Link(
    [StructField(StringFormat = ""ipv4"")] string Address);";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-011.3")]
        public async Task ReportReservedTypeKindFormatOnStringStructField()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Link(
    [{|#0:StructField(StringFormat = ""uuid"")|}] string Address);";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Address", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }
    }
}