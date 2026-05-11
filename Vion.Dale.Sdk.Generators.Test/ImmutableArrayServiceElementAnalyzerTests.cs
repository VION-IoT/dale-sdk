using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ImmutableArrayServiceElementAnalyzerTests
    {
        // --- Types that should trigger DALE008 ---

        [TestMethod]
        public async Task IntArray_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int[] {|#0:Values|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray).WithLocation(0).WithArguments("Values", "ServiceProperty", "int[]");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ListOfDouble_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public List<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceProperty", "List<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task IReadOnlyListOfInt_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public IReadOnlyList<int> {|#0:Values|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray)
                                           .WithLocation(0)
                                           .WithArguments("Values", "ServiceProperty", "IReadOnlyList<int>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task IEnumerableOfDouble_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public IEnumerable<double> {|#0:Samples|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceProperty", "IEnumerable<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [DataRow("IList<int>", "IList<int>")]
        [DataRow("ICollection<int>", "ICollection<int>")]
        [DataRow("IReadOnlyCollection<int>", "IReadOnlyCollection<int>")]
        [TestMethod]
        public async Task OtherMutableCollections_ReportsDiagnostic(string typeName, string expectedTypeName)
        {
            var source = $@"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{{
    [ServiceProperty] public {typeName} {{|#0:Values|}} {{ get; set; }}
}}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray)
                                           .WithLocation(0)
                                           .WithArguments("Values", "ServiceProperty", expectedTypeName);
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MeasuringPoint_ListOfDouble_ReportsDiagnostic()
        {
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public List<double> {|#0:Samples|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray)
                                           .WithLocation(0)
                                           .WithArguments("Samples", "ServiceMeasuringPoint", "List<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        // --- Types that should NOT trigger DALE008 ---

        [TestMethod]
        public async Task ImmutableArrayOfDouble_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public ImmutableArray<double> Samples { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task PropertyWithoutAttribute_IntArray_NoDiagnostic()
        {
            var source = @"
public class MyBlock
{
    public int[] Values { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source);
        }

        [TestMethod]
        public async Task NonCollectionType_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int Value { get; set; }
    [ServiceProperty] public double Rate { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source);
        }
    }
}