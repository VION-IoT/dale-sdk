using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class WriteOnlyStructFieldTypeRestrictionAnalyzerTests
    {
        [TestMethod]
        public async Task WriteOnlyOnStringField_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Credentials(
    [StructField(Title = ""Endpoint"")] string Endpoint,
    [StructField(WriteOnly = true)] string AccessToken);";
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyStructFieldTypeRestrictionAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyOnNullableStringField_NoDiagnostic()
        {
            // string? shares SpecialType.System_String — v1 allows string and string?.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Credentials(
    [StructField(WriteOnly = true)] string? AccessToken);";
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyStructFieldTypeRestrictionAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyUnset_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Sample(
    [StructField(Unit = ""kW"")] double Power);";
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyStructFieldTypeRestrictionAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyOnNonStringField_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Sample(
    [StructField(WriteOnly = true)] int {|#0:Secret|});";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE040_WriteOnlyStructFieldTypeRestriction).WithLocation(0).WithArguments("Secret", "int");
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyStructFieldTypeRestrictionAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task WriteOnlyOnEnumField_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public enum Mode { A, B }

public readonly record struct Sample(
    [StructField(WriteOnly = true)] Mode {|#0:Selected|});";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE040_WriteOnlyStructFieldTypeRestriction).WithLocation(0).WithArguments("Selected", "Mode");
            await AnalyzerTestBase.VerifyAnalyzerAsync<WriteOnlyStructFieldTypeRestrictionAnalyzer>(source, expected);
        }
    }
}