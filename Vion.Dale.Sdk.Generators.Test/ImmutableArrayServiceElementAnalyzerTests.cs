using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ImmutableArrayServiceElementAnalyzerTests
    {
        // --- Types that should trigger DALE008 ---

        [DataRow("int[]", DisplayName = "array")]
        [DataRow("List<double>", DisplayName = "List")]
        [DataRow("IList<int>", DisplayName = "IList")]
        [DataRow("ICollection<int>", DisplayName = "ICollection")]
        [DataRow("IEnumerable<double>", DisplayName = "IEnumerable")]
        [DataRow("IReadOnlyList<int>", DisplayName = "IReadOnlyList")]
        [DataRow("IReadOnlyCollection<int>", DisplayName = "IReadOnlyCollection")]
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.7")]
        public async Task ReportCollectionShapeOutsideImmutableArray(string typeName)
        {
            // Arrange / Act / Assert
            // The seven shapes the rule ranges over. Read-only is not immutable: IReadOnlyList<T> is a
            // view whose backing store the holder can still mutate, so it is refused with the rest.
            var source = $@"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{{
    [ServiceProperty] public {typeName} {{|#0:Values|}} {{ get; set; }}
}}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray).WithLocation(0).WithArguments("Values", "ServiceProperty", typeName);
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.7")]
        public async Task ReportMutableListOnMeasuringPoint()
        {
            // Arrange / Act / Assert
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

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.2")]
        public async Task ReportBothSupportedTypeRulesOnOneDeclaration()
        {
            // Arrange
            // List<double> breaks two of the three rules at once: DALE003 (not a supported service-element
            // type) and DALE008 (a collection that is not an ImmutableArray). Two analyzers, one
            // declaration — neither stands down for the other, so the author is told both things.
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public List<double> Bands { get; set; }
}";

            // Act
            var reported = await AnalyzerTestBase.RunAnalyzersAsync(source, new ServiceElementTypeAnalyzer(), new ImmutableArrayServiceElementAnalyzer());

            // Assert
            CollectionAssert.AreEqual(new[] { DaleDiagnostics.DALE003_UnsupportedServicePropertyType.Id, DaleDiagnostics.DALE008_ArrayMustBeImmutableArray.Id },
                                      reported.Select(d => d.Id).ToArray(),
                                      "each supported-type rule reports on its own: " + string.Join("; ", reported.Select(d => d.GetMessage())));
        }

        // --- Types that should NOT trigger DALE008 ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.7")]
        public async Task StaySilentOnImmutableArray()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-003.7")]
        public async Task StaySilentOnArrayWithoutServiceAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
public class MyBlock
{
    public int[] Values { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmutableArrayServiceElementAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.7")]
        public async Task StaySilentOnNonCollectionType()
        {
            // Arrange / Act / Assert
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