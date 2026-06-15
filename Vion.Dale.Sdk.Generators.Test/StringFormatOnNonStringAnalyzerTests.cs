using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class StringFormatOnNonStringAnalyzerTests
    {
        [TestMethod]
        public async Task StringFormatOnString_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(StringFormat = ""ipv4"")] public string Ip { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source);
        }

        [TestMethod]
        public async Task StringFormatUnset_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string Notes { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source);
        }

        [TestMethod]
        public async Task StringFormatOnNonString_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(StringFormat = ""ipv4"")] public int {|#0:Port|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Port", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ReservedFormatValueOnString_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(StringFormat = ""uuid"")] public string {|#0:Id|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE033_StringFormatOnNonString).WithLocation(0).WithArguments("Id", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<StringFormatOnNonStringAnalyzer>(source, expected);
        }
    }
}