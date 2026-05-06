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
    }
}