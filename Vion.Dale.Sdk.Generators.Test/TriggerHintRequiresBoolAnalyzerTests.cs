using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class TriggerHintRequiresBoolAnalyzerTests
    {
        [TestMethod]
        public async Task TriggerOnWritableBool_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(UiHint = ""trigger"")] public bool DoSomething { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TriggerHintRequiresBoolAnalyzer>(source);
        }

        [TestMethod]
        public async Task TriggerOnNonBool_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(UiHint = ""trigger"")] public int {|#0:Counter|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE023_TriggerHintRequiresBool).WithLocation(0).WithArguments("Counter", "type 'int' is not bool");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TriggerHintRequiresBoolAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task TriggerOnReadOnlyBool_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(UiHint = ""trigger"")] public bool {|#0:Flag|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE023_TriggerHintRequiresBool).WithLocation(0).WithArguments("Flag", "the property is read-only");
            await AnalyzerTestBase.VerifyAnalyzerAsync<TriggerHintRequiresBoolAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NonTriggerHint_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(UiHint = ""sparkline"")] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TriggerHintRequiresBoolAnalyzer>(source);
        }
    }
}
