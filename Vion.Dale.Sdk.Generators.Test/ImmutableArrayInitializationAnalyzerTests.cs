using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ImmutableArrayInitializationAnalyzerTests
    {
        // --- Missing initializer: should trigger DALE018 ---

        [TestMethod]
        public async Task ImmutableArrayWithoutInitializer_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE018_ImmutableArrayMustBeInitialised).WithLocation(0).WithArguments("Samples", "ServiceProperty");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MeasuringPoint_ImmutableArrayWithoutInitializer_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public ImmutableArray<int> {|#0:Values|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE018_ImmutableArrayMustBeInitialised).WithLocation(0).WithArguments("Values", "ServiceMeasuringPoint");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source, expected);
        }

        // --- With initializer: should NOT trigger DALE018 ---

        [TestMethod]
        public async Task ImmutableArrayWithEmptyInitializer_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<double> Samples { get; set; } = ImmutableArray<double>.Empty;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayWithCreateInitializer_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<int> Values { get; set; } = ImmutableArray.Create(1, 2, 3);
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        // --- Explicit accessors: opaque getter, can't carry a property initializer → should NOT trigger DALE018 ---

        [TestMethod]
        public async Task ImmutableArrayWithExplicitGetterAndInitializedBackingField_NoDiagnostic()
        {
            // DALE018 targets auto-implemented properties, whose compiler backing field defaults to a throwing
            // default ImmutableArray. A property with an explicit getter returning an initialized backing field
            // can never return default — and explicit accessors can't carry a property-level initializer anyway,
            // so the old property-initializer-only check produced a false positive here.
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    private ImmutableArray<int> _plan = ImmutableArray<int>.Empty;

    [ServiceProperty]
    public ImmutableArray<int> Plan
    {
        get => _plan;
        set => _plan = value.IsDefault ? ImmutableArray<int>.Empty : value;
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayExpressionBodiedProperty_NoDiagnostic()
        {
            // An expression-bodied (computed) getter is opaque — the developer owns what it returns.
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    private ImmutableArray<int> _plan = ImmutableArray<int>.Empty;

    [ServiceMeasuringPoint] public ImmutableArray<int> Plan => _plan;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmutableArrayWithBlockBodiedGetter_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    private ImmutableArray<int> _plan = ImmutableArray<int>.Empty;

    [ServiceProperty]
    public ImmutableArray<int> Plan
    {
        get { return _plan; }
        set { _plan = value; }
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        // --- Non-ImmutableArray types: should NOT trigger DALE018 ---

        [TestMethod]
        public async Task IntProperty_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int Value { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        // --- No attribute: should NOT trigger DALE018 ---

        [TestMethod]
        public async Task ImmutableArrayWithoutAttribute_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;

public class MyBlock
{
    public ImmutableArray<double> Samples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        // --- Interface / abstract members: can't carry an initializer; obligation is on the impl ---

        [TestMethod]
        public async Task InterfaceMember_ImmutableArrayWithoutInitializer_NoDiagnostic()
        {
            // An interface property can't have an initializer (compile error), so DALE018 here would be
            // unactionable. The check belongs on the implementing block.
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public interface IMyService
{
    [ServiceProperty] ImmutableArray<double> Samples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }

        [TestMethod]
        public async Task AbstractProperty_ImmutableArrayWithoutInitializer_NoDiagnostic()
        {
            // An abstract auto-property can't have an initializer either; the concrete override initialises.
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public abstract class MyBaseBlock
{
    [ServiceProperty] public abstract ImmutableArray<double> Samples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayInitializationAnalyzer>(source);
        }
    }
}