using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE041 / DALE042 coverage for <c>VisibleWhenPredicateAnalyzer</c> (RFC 0017). Positive cases
    ///     assert a well-formed predicate produces no diagnostic; negative cases assert the right
    ///     diagnostic id fires at the predicate string (the message text is intentionally not pinned).
    /// </summary>
    [TestClass]
    public class VisibleWhenPredicateAnalyzerTests
    {
        // A block with a root service + two component services of the same type, used by most cases.
        private const string Scaffold = @"
using Vion.Dale.Sdk.Core;

public enum Mode { Eco, Fast, Off }

public class ChargingPoint
{
    [ServiceProperty] public bool EnableCharging { get; set; }
    [ServiceProperty] public int Priority { get; set; }
}

public class MyBlock : LogicBlockBase
{
    [ServiceProperty] public bool DirectMeasurement { get; set; }
    [ServiceProperty] public int NumChargingPoints { get; set; }
    [ServiceProperty] public Mode Mode { get; set; }
    [ServiceProperty] public bool IsExternallyLocked { get; set; }

    public ChargingPoint ChargingPoint1 { get; set; } = new();
    public ChargingPoint ChargingPoint2 { get; set; } = new();

    {0}
}";

        private static string Block(string annotatedMember)
        {
            return Scaffold.Replace("{0}", annotatedMember);
        }

        // ── Positive cases (no diagnostic) ──

        [TestMethod]
        [DataRow("DirectMeasurement == false", DisplayName = "bare bool comparison")]
        [DataRow("DirectMeasurement", DisplayName = "bare bool ref")]
        [DataRow("!DirectMeasurement", DisplayName = "negated bare bool ref")]
        [DataRow("Mode == 'Eco'", DisplayName = "quoted enum comparison")]
        [DataRow("Mode != 'Off'", DisplayName = "quoted enum inequality")]
        [DataRow("Mode in ['Eco', 'Fast']", DisplayName = "enum membership")]
        [DataRow("NumChargingPoints > 1", DisplayName = "integer relational")]
        [DataRow("NumChargingPoints in [1, 2, 3]", DisplayName = "integer membership")]
        [DataRow("DirectMeasurement == false && NumChargingPoints > 1", DisplayName = "conjunction")]
        [DataRow("!(DirectMeasurement || IsExternallyLocked)", DisplayName = "negated parenthesized disjunction")]
        [DataRow("ChargingPoint2.EnableCharging == true", DisplayName = "qualified sibling-service ref")]
        [DataRow("MyBlock.IsExternallyLocked == false", DisplayName = "qualified root-service ref by class name")]
        public async Task ValidPredicate_NoDiagnostic(string predicate)
        {
            var source = Block($"[ServiceProperty] [Presentation(VisibleWhen = \"{predicate}\")] public double PrimaryCurrentToWriteA {{ get; set; }}");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        public async Task VisibleWhenOnMeasuringPoint_NoDiagnostic()
        {
            // Measuring points can carry VisibleWhen (a deliberate extension of RFC 0017).
            var source = Block("[ServiceMeasuringPoint] [Presentation(VisibleWhen = \"DirectMeasurement == false\")] public int Power { get; private set; }");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        public async Task NoVisibleWhen_NoDiagnostic()
        {
            var source = Block("[ServiceProperty] [Presentation(Group = PropertyGroup.Configuration)] public double PrimaryCurrentToWriteA { get; set; }");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        public async Task PredicateOnComponentMember_ReferencingRoot_NoDiagnostic()
        {
            // A predicate authored inside a component service, addressing the root by class name.
            var source = @"
using Vion.Dale.Sdk.Core;

public class ChargingPoint
{
    [ServiceProperty] public bool EnableCharging { get; set; }
    [ServiceProperty] [Presentation(VisibleWhen = ""MyBlock.IsExternallyLocked == false"")] public int Current { get; set; }
}

public class MyBlock : LogicBlockBase
{
    [ServiceProperty] public bool IsExternallyLocked { get; set; }
    public ChargingPoint ChargingPoint1 { get; set; } = new();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        // ── Negative cases: DALE041 (parse / resolve) ──

        [TestMethod]
        public async Task Arithmetic_ReportsDALE041()
        {
            await ExpectDiagnostic("NumChargingPoints * 2 == 4", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        public async Task ThreeSegmentRef_ReportsDALE041()
        {
            await ExpectDiagnostic("ChargingPoint2.Meter.Voltage == 1", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        public async Task UnresolvedBareRef_ReportsDALE041()
        {
            await ExpectDiagnostic("Nonexistent == false", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        public async Task UnknownQualifiedService_ReportsDALE041()
        {
            await ExpectDiagnostic("NoSuchService.Foo == 1", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        public async Task MeasuringPointOnlyReference_ReportsDALE041()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [ServiceMeasuringPoint] public int Power { get; private set; }
    [ServiceProperty] [Presentation(VisibleWhen = {|#0:""Power == 1""|})] public bool X { get; set; }
}";
            var expected = Diag(DaleDiagnostics.DALE041_VisibleWhenUnresolved).WithLocation(0);
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ShadowedQualifiedRef_ReportsDALE041()
        {
            // Comp has a property 'Point2' that collides with the sibling-service identifier 'Point2'.
            var source = @"
using Vion.Dale.Sdk.Core;

public class Comp
{
    [ServiceProperty] public bool Point2 { get; set; }
    [ServiceProperty] public int Value { get; set; }
    [ServiceProperty] [Presentation(VisibleWhen = {|#0:""Point2.Value == 1""|})] public bool X { get; set; }
}

public class MyBlock : LogicBlockBase
{
    public Comp Point1 { get; set; } = new();
    public Comp Point2 { get; set; } = new();
}";
            var expected = Diag(DaleDiagnostics.DALE041_VisibleWhenUnresolved).WithLocation(0);
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        // ── Negative cases: DALE042 (type discipline) ──

        [TestMethod]
        public async Task UnquotedEnumMember_ReportsDALE042()
        {
            await ExpectDiagnostic("Mode == Eco", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        public async Task BareNonBoolRef_ReportsDALE042()
        {
            await ExpectDiagnostic("NumChargingPoints", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        public async Task RelationalOnNonInteger_ReportsDALE042()
        {
            await ExpectDiagnostic("DirectMeasurement > 1", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        public async Task EqualityLiteralTypeMismatch_ReportsDALE042()
        {
            await ExpectDiagnostic("NumChargingPoints == true", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        public async Task DoubleReference_ReportsDALE042()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [ServiceProperty] public double Analog { get; set; }
    [ServiceProperty] [Presentation(VisibleWhen = {|#0:""Analog == 1""|})] public bool X { get; set; }
}";
            var expected = Diag(DaleDiagnostics.DALE042_VisibleWhenTypeMismatch).WithLocation(0);
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task WriteOnlyReference_ReportsDALE042()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock : LogicBlockBase
{
    [ServiceProperty(WriteOnly = true)] public string Secret { get; set; } = """";
    [ServiceProperty] [Presentation(VisibleWhen = {|#0:""Secret == 'x'""|})] public bool X { get; set; }
}";
            var expected = Diag(DaleDiagnostics.DALE042_VisibleWhenTypeMismatch).WithLocation(0);
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        // ── Cross-assembly (NuGet-referenced) component type ──

        [TestMethod]
        public async Task QualifiedRefToComponentFromReferencedAssembly_NoDiagnostic()
        {
            // The component type (and its [ServiceProperty] members) live in metadata, not source —
            // GetMembers()/attribute reads must resolve them the same way.
            var libReference = await CompileLibraryAsync(GetAttributeStubs() + @"
namespace Lib
{
    using Vion.Dale.Sdk.Core;

    public class ChargingPoint
    {
        [ServiceProperty] public bool EnableCharging { get; set; }
    }
}");

            var consumerSource = @"
using Vion.Dale.Sdk.Core;
using Lib;

public class MyBlock : LogicBlockBase
{
    public ChargingPoint ChargingPoint1 { get; set; } = new();
    [ServiceProperty] [Presentation(VisibleWhen = ""ChargingPoint1.EnableCharging == true"")] public bool X { get; set; }
}";

            await VerifyWithReferenceNoStubsAsync(consumerSource, libReference);
        }

        // ── Helpers ──

        // Build the expected result from the descriptor id only, so the harness verifies the diagnostic
        // id + location but not the (deliberately detailed, non-pinned) reason message.
        private static DiagnosticResult Diag(DiagnosticDescriptor descriptor)
        {
            return new DiagnosticResult(descriptor.Id, DiagnosticSeverity.Error);
        }

        private static async Task ExpectDiagnostic(string predicate, DiagnosticDescriptor descriptor)
        {
            var source = Block($"[ServiceProperty] [Presentation(VisibleWhen = {{|#0:\"{predicate}\"|}})] public bool X {{ get; set; }}");
            var expected = Diag(descriptor).WithLocation(0);
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        private static string GetAttributeStubs()
        {
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(VisibleWhenPredicateAnalyzerTests).Assembly.Location)!, "..", "..", "..", "Helpers", "TestAttributeStubs.cs");
            return File.ReadAllText(stubsPath);
        }

        private static async Task<MetadataReference> CompileLibraryAsync(string source)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var compilation = CSharpCompilation.Create("Lib",
                                                       new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                                                       refs,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            Assert.IsTrue(emit.Success, "Lib compilation failed:\n" + string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            stream.Position = 0;
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        // The consumer references the library for the Dale attributes (single definition — no duplicate
        // stubs), so its own block can use them and the component type comes from metadata.
        private static async Task VerifyWithReferenceNoStubsAsync<TAnalyzer>(string source, MetadataReference reference)
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
                       {
                           TestCode = source,
                           ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                       };
            test.TestState.AdditionalReferences.Add(reference);
            test.SolutionTransforms.Add((solution, projectId) =>
                                        {
                                            var project = solution.GetProject(projectId)!;
                                            var parseOptions = (CSharpParseOptions)project.ParseOptions!;
                                            return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(LanguageVersion.Latest));
                                        });
            await test.RunAsync();
        }

        private static async Task VerifyWithReferenceNoStubsAsync(string source, MetadataReference reference)
        {
            await VerifyWithReferenceNoStubsAsync<VisibleWhenPredicateAnalyzer>(source, reference);
        }
    }
}
