using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ImportanceRequiresScalarTypeAnalyzerTests
    {
        // --- Composite types at Primary/Secondary → DALE032 ---

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task ReportPrimaryImportanceOnRecordStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Range(double Min, double Max);

public class MyBlock
{
    [Presentation(Importance = Importance.Primary)] public Range {|#0:Span|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE032_ImportanceRequiresScalarType).WithLocation(0).WithArguments("Span", "Primary", "Range");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task ReportSecondaryImportanceOnImmutableArray()
        {
            // Arrange / Act / Assert
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Importance = Importance.Secondary)] public ImmutableArray<int> {|#0:Values|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE032_ImportanceRequiresScalarType)
                                           .WithLocation(0)
                                           .WithArguments("Values", "Secondary", "ImmutableArray<int>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task ReportPrimaryImportanceOnNullableRecordStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Range(double Min, double Max);

public class MyBlock
{
    [Presentation(Importance = Importance.Primary)] public Range? {|#0:Span|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE032_ImportanceRequiresScalarType).WithLocation(0).WithArguments("Span", "Primary", "Range?");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source, expected);
        }

        // --- Scalar types at Primary/Secondary → no diagnostic ---

        [DataRow("int")]
        [DataRow("double")]
        [DataRow("bool")]
        [DataRow("string")]
        [DataRow("System.DateTime")]
        [DataRow("System.TimeSpan")]
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task StaySilentOnPrimaryImportanceOverScalar(string typeName)
        {
            // Arrange / Act / Assert
            var source = $@"
using Vion.Dale.Sdk.Core;

public class MyBlock
{{
    [Presentation(Importance = Importance.Primary)] public {typeName} Value {{ get; set; }}
}}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task StaySilentOnPrimaryImportanceOverEnum()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public enum Mode { Off, On }

public class MyBlock
{
    [Presentation(Importance = Importance.Primary)] public Mode Current { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source);
        }

        // --- Composite type, but not a tile importance → no diagnostic ---

        [DataRow("Importance.Normal")]
        [DataRow("Importance.Hidden")]
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task StaySilentOnNormalImportanceOverRecordStruct(string importance)
        {
            // Arrange / Act / Assert
            var source = $@"
using Vion.Dale.Sdk.Core;

public readonly record struct Range(double Min, double Max);

public class MyBlock
{{
    [Presentation(Importance = {importance})] public Range Span {{ get; set; }}
}}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task StaySilentOnRecordStructWithoutPresentation()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Range(double Min, double Max);

public class MyBlock
{
    public Range Span { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.6")]
        public async Task StaySilentWhenImportanceUnset()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Range(double Min, double Max);

public class MyBlock
{
    [Presentation(DisplayName = ""Span"")] public Range Span { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImportanceRequiresScalarTypeAnalyzer>(source);
        }
    }
}