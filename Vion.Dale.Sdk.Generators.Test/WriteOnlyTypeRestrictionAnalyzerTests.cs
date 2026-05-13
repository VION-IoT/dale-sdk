using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class WriteOnlyTypeRestrictionAnalyzerTests
    {
        [TestMethod]
        public async Task WriteOnlyOnString_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(WriteOnly = true)] public string ApiKey { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyTypeRestrictionAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyFalse_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyTypeRestrictionAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyOnInt_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(WriteOnly = true)] public int {|#0:Counter|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE022_WriteOnlyTypeRestriction).WithLocation(0).WithArguments("Counter", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyTypeRestrictionAnalyzer>(source, expected);
        }
    }
}
