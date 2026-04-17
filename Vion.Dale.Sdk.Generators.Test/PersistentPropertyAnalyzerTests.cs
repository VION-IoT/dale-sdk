using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class PersistentPropertyAnalyzerTests
    {
        [TestMethod]
        public async Task PersistentWithSetter_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        public async Task PersistentWithPrivateSetter_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int Counter { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        public async Task PersistentGetOnly_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent] public int {|#0:Counter|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE007_PersistentRequiresSetter)
                .WithLocation(0)
                .WithArguments("Counter");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task PersistentExcludeTrue_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Persistent(Exclude = true)] public int Counter { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }

        [TestMethod]
        public async Task NoPersistentAttribute_NoDiagnostic()
        {
            var source = @"
public class MyBlock
{
    public int Counter { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PersistentPropertyAnalyzer>(source);
        }
    }
}
