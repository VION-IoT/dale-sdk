using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class TriggerHintRequiresBoolAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.3")]
        public async Task StaySilentOnTriggerOverWritableBool()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(UiHint = ""trigger"")] public bool DoSomething { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<TriggerHintRequiresBoolAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.3")]
        public async Task ReportTriggerOnNonBool()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-009.3")]
        public async Task ReportTriggerOnReadOnlyBool()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-009.3")]
        public async Task StaySilentOnOtherUiHint()
        {
            // Arrange / Act / Assert
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