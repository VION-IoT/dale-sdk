using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class DecimalsOnNonNumericAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.2")]
        public async Task StaySilentOnDecimalsOverNumericProperty()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Decimals = 2)] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.2")]
        public async Task StaySilentWhenDecimalsUnset()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""status"")] public string Status { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.2")]
        public async Task ReportDecimalsOnNonNumericProperty()
        {
            // Arrange / Act / Assert
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