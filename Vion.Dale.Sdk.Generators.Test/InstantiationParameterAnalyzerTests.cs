using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE044 coverage for <c>InstantiationParameterAnalyzer</c>. Positive cases assert
    ///     a well-formed parameter produces no diagnostic; negative cases assert DALE044 fires (the message
    ///     is intentionally not pinned).
    /// </summary>
    [TestClass]
    public class InstantiationParameterAnalyzerTests
    {
        // ── Positive cases (no diagnostic) ──

        [TestMethod]
        public async Task WellFormedScalarParameters_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public enum StationModel { Bricco, Moka }
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [ServiceProperty] [InstantiationParameter] public bool HasOutlet { get; init; }
    [ServiceProperty] [InstantiationParameter] public string Region { get; init; }
    [ServiceProperty] [InstantiationParameter] public StationModel Model { get; init; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.9")]
        public async Task PlainSetterParameter_NoDiagnostic()
        {
            // { get; set; } is allowed (init is recommended, not required — the analyzer backstops assignments).
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; set; }
    public MyBlock() { Count = 1; } // assignment in the constructor is allowed
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source);
        }

        [TestMethod]
        public async Task ParameterDeclaredOnABaseClass_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class BaseStation : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
}
public class LeafStation : BaseStation
{
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source);
        }

        // ── Negative cases (DALE044) ──

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task MissingServicePropertyPairing_ReportsDALE044()
        {
            return ExpectDiscipline("[{|#0:InstantiationParameter|}] public int Count { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task DisallowedDoubleType_ReportsDALE044()
        {
            return ExpectDiscipline("[ServiceProperty] [{|#0:InstantiationParameter|}] public double Count { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task DisallowedArrayType_ReportsDALE044()
        {
            return ExpectDiscipline("[ServiceProperty] [{|#0:InstantiationParameter|}] public int[] Values { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public async Task DisallowedStructType_ReportsDALE044()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public readonly record struct Coords(int X, int Y);
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [{|#0:InstantiationParameter|}] public Coords Position { get; init; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, Diag().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.11")]
        public async Task PersistentExcludedParameter_NoDiagnostic()
        {
            // Arrange
            // [Persistent(Exclude = true)] is the documented opt-OUT, so it asks for exactly what a parameter
            // already gets. Refusing it would reject a correct declaration with a reason that is false for it.
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [Persistent(Exclude = true)] [InstantiationParameter] public int Count { get; init; }
}";

            // Act / Assert
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task PersistentParameter_ReportsDALE044()
        {
            // Persistence skips parameters, so the [Persistent] would silently do nothing — and a restore
            // that did land would overwrite the configured value the gates already resolved against.
            return ExpectDiscipline("[ServiceProperty] [Persistent] [{|#0:InstantiationParameter|}] public int Count { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task PersistentNotExcludedParameter_ReportsDALE044()
        {
            // Exclude = false is the opt-IN written out, so it is refused exactly like the bare attribute —
            // the rule reads the argument rather than the attribute's presence.
            return ExpectDiscipline("[ServiceProperty] [Persistent(Exclude = false)] [{|#0:InstantiationParameter|}] public int Count { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task WriteOnlyParameter_ReportsDALE044()
        {
            return ExpectDiscipline("[ServiceProperty(WriteOnly = true)] [{|#0:InstantiationParameter|}] public string Secret { get; init; }");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public Task ComputedGetter_ReportsDALE044()
        {
            return ExpectDiscipline("[ServiceProperty] [{|#0:InstantiationParameter|}] public int Count => 3;");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public async Task InCodeAssignmentOutsideConstructor_ReportsDALE044()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; set; }
    public void Bump() { {|#0:Count = 5|}; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, Diag().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.8")]
        public async Task DeclaredOnAComponentType_ReportsDALE044()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class Component
{
    [ServiceProperty] [{|#0:InstantiationParameter|}] public int Offset { get; init; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, Diag().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-011.4")]
        public async Task RedeclaredOnAnOverride_ReportsDALE044()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class BaseBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public virtual int Count { get; set; }
}
public class LeafBlock : BaseBlock
{
    [ServiceProperty] [{|#0:InstantiationParameter|}] public override int Count { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, Diag().WithLocation(0));
        }

        // ── Helpers ──

        private static DiagnosticResult Diag()
        {
            return new DiagnosticResult(DaleDiagnostics.DALE044_InstantiationParameterDiscipline.Id, DiagnosticSeverity.Error);
        }

        private static async Task ExpectDiscipline(string parameterMember)
        {
            var source = $@"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{{
    {parameterMember}
}}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, Diag().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-014.2")]
        [DataRow("internal", DisplayName = "internal")]
        [DataRow("protected", DisplayName = "protected")]
        [DataRow("private", DisplayName = "private")]
        public async Task WarnOnInstantiationParameterThatIsNotPublic(string accessibility)
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [{|#0:InstantiationParameter|}]
    [ServiceProperty]
    " + accessibility + @" bool Hidden { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE044_InstantiationParameterDiscipline)
                                           .WithLocation(0)
                                           .WithSeverity(DiagnosticSeverity.Warning)
                                           .WithArguments("Hidden",
                                                          "[InstantiationParameter] must be a public property — the binders read a block's parameters from its public " +
                                                          "instance properties, so a non-public one is configured by nothing and no [IncludedWhen] gate can resolve to it.");
            await AnalyzerTestBase.VerifyAnalyzerAsync<InstantiationParameterAnalyzer>(source, expected);
        }
    }
}