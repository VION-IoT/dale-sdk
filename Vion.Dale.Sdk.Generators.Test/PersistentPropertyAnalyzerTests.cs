using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class PersistentPropertyAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.2")]
        public async Task StaySilentOnPersistentWithSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.2")]
        public async Task StaySilentOnPersistentWithPrivateSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int Counter { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.2")]
        public async Task ReportPersistentWithoutSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int {|#0:Counter|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE007_PersistentRequiresSetter).WithLocation(0).WithArguments("Counter");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.2")]
        public async Task StaySilentOnExcludedPersistent()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent(Exclude = true)] public int Counter { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.2")]
        public async Task StaySilentWithoutPersistentAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
public class MyBlock
{
    public int Counter { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }
    }
}