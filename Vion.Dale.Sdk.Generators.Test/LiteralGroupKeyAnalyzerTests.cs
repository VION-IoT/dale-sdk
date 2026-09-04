using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class LiteralGroupKeyAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.2")]
        public async Task StaySilentOnConstantReference()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = PropertyGroup.Status)] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<LiteralGroupKeyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.1")]
        public async Task StaySilentOnLiteralMatchingConstantDeclaredInSource()
        {
            // Arrange / Act / Assert
            // ""status"" matches Vion.Dale.Sdk.Core.PropertyGroup.Status.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""status"")] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<LiteralGroupKeyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.1")]
        public async Task ReportLiteralMatchingNoConstant()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = {|#0:""acme.powertrain""|})] public int Counter { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE026_LiteralGroupKey).WithLocation(0).WithArguments("acme.powertrain");
            await AnalyzerTestBase.VerifyAnalyzerAsync<LiteralGroupKeyAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.5")]
        public async Task StaySilentOnLiteralMatchingIntegratorConstant()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

namespace Acme
{
    public static class PropertyGroup
    {
        public const string Powertrain = ""acme.powertrain"";
    }
}

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<LiteralGroupKeyAnalyzer>(source);
        }
    }
}