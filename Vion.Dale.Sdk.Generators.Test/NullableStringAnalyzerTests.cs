using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class NullableStringAnalyzerTests
    {
        // --- Nullable-disabled context: should trigger DALE017 ---

        [TestMethod]
        public async Task NullableDisabled_StringProperty_ReportsDiagnostic()
        {
            var source = @"
#nullable disable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string {|#0:Name|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE017_StringMustBeExplicitlyNullable).WithLocation(0).WithArguments("Name", "ServiceProperty");
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NullableDisabled_MeasuringPoint_StringProperty_ReportsDiagnostic()
        {
            var source = @"
#nullable disable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public string {|#0:Label|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE017_StringMustBeExplicitlyNullable).WithLocation(0).WithArguments("Label", "ServiceMeasuringPoint");
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source, expected);
        }

        // --- Nullable-disabled context but string? annotation: should NOT trigger ---

        [TestMethod]
        public async Task NullableDisabled_NullableStringAnnotation_NoDiagnostic()
        {
            // Even with #nullable disable, using string? sets the annotation — no DALE017.
            var source = @"
#nullable disable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string? Name { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source);
        }

        // --- Nullable-enabled context: should NOT trigger DALE017 ---

        [TestMethod]
        public async Task NullableEnabled_NonNullString_NoDiagnostic()
        {
            var source = @"
#nullable enable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string Name { get; set; } = string.Empty;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source);
        }

        [TestMethod]
        public async Task NullableEnabled_NullableString_NoDiagnostic()
        {
            var source = @"
#nullable enable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public string? Name { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source);
        }

        // --- Non-string types: should NOT trigger DALE017 ---

        [TestMethod]
        public async Task NullableDisabled_IntProperty_NoDiagnostic()
        {
            var source = @"
#nullable disable
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public int Value { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source);
        }

        // --- No attribute: should NOT trigger DALE017 ---

        [TestMethod]
        public async Task NoAttribute_StringProperty_NoDiagnostic()
        {
            var source = @"
#nullable disable

public class MyBlock
{
    public string Name { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<NullableStringAnalyzer>(source);
        }
    }
}