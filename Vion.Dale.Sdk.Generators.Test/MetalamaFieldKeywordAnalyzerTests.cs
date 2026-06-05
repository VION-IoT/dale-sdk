using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MetalamaFieldKeywordAnalyzerTests
    {
        [TestMethod]
        public async Task FieldKeywordSetter_OnLogicBlockBaseSubclass_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [ServiceProperty]
    public int {|#0:Counter|}
    {
        get;
        set
        {
            field = value;
            SideEffect();
        }
    }

    private void SideEffect() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE029_MetalamaFieldKeywordSetter).WithLocation(0).WithArguments("Counter");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task FieldKeywordSetter_WithoutServicePropertyAttribute_StillReportsDiagnostic()
        {
            // [Observable] is applied at the TYPE level via MetalamaTransitiveFabric, so the bug
            // hits ANY property on a LogicBlockBase subclass — not just [ServiceProperty]-marked.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    public int {|#0:Counter|}
    {
        get;
        set
        {
            field = value;
            SideEffect();
        }
    }

    private void SideEffect() { }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE029_MetalamaFieldKeywordSetter).WithLocation(0).WithArguments("Counter");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ExplicitBackingFieldSetter_OnLogicBlockBaseSubclass_NoDiagnostic()
        {
            // The recommended fix pattern: use an explicit private backing field; the [Observable]
            // aspect handles this shape correctly.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    private int _counter;

    [ServiceProperty]
    public int Counter
    {
        get => _counter;
        set
        {
            _counter = value;
            SideEffect();
        }
    }

    private void SideEffect() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source);
        }

        [TestMethod]
        public async Task AutoPropertySetter_OnLogicBlockBaseSubclass_NoDiagnostic()
        {
            // Auto-property — no body to drop. [Observable] handles these.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [ServiceProperty]
    public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source);
        }

        [TestMethod]
        public async Task FieldKeywordGetter_GetterOnly_NoDiagnostic()
        {
            // Getter-side use of 'field' keyword: setter doesn't exist, so the aspect's setter
            // rewrite isn't engaged. The analyzer only targets setters.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    public int Counter
    {
        get => field;
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source);
        }

        [TestMethod]
        public async Task FieldKeywordSetter_OnNonLogicBlockBaseType_NoDiagnostic()
        {
            // The Metalama transitive fabric only applies [Observable] to LogicBlockBase subclasses,
            // so the bug doesn't affect regular classes — no warning needed.
            var source = @"
public class PlainClass
{
    public int Counter
    {
        get;
        set
        {
            field = value;
        }
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MetalamaFieldKeywordAnalyzer>(source);
        }
    }
}