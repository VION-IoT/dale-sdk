using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class DecimalsOnNonNumericAnalyzerTests
    {
        [TestMethod]
        public async Task DecimalsOnDouble_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Decimals = 2)] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source);
        }

        [TestMethod]
        public async Task DecimalsUnset_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""status"")] public string Status { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source);
        }

        [TestMethod]
        public async Task DecimalsOnString_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Decimals = 2)] public string {|#0:Status|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE021_DecimalsOnNonNumeric).WithLocation(0).WithArguments("Status", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source, expected);
        }
    }
}
