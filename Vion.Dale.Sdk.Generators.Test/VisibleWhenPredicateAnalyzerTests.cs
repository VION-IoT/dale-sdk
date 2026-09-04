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
    ///     DALE041 / DALE042 coverage for <c>VisibleWhenPredicateAnalyzer</c>. Positive cases
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

        // ── Positive cases (no diagnostic) ──

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.5")]
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
        public async Task StaySilentOnResolvingPredicate(string predicate)
        {
            // Arrange / Act / Assert
            var source = Block($"[ServiceProperty] [Presentation(VisibleWhen = \"{predicate}\")] public double PrimaryCurrentToWriteA {{ get; set; }}");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.4")]
        public async Task JudgePredicateOnMeasuringPoint()
        {
            // Arrange / Act / Assert
            // Measuring points can carry VisibleWhen, deliberately: the predicate rides both documents
            // of a member declaring both streams.
            var source = Block("[ServiceMeasuringPoint] [Presentation(VisibleWhen = \"DirectMeasurement == false\")] public int Power { get; private set; }");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.4")]
        public async Task StaySilentWithoutPredicate()
        {
            // Arrange / Act / Assert
            var source = Block("[ServiceProperty] [Presentation(Group = PropertyGroup.Configuration)] public double PrimaryCurrentToWriteA { get; set; }");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.4")]
        public async Task ResolveComponentPredicateAgainstItsHolder()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportArithmeticInPredicate()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NumChargingPoints * 2 == 4", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportThreeSegmentReference()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("ChargingPoint2.Meter.Voltage == 1", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportOverflowingIntegerLiteral()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NumChargingPoints == 9999999999", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportLiteralOnLeftOfComparison()
        {
            // Arrange / Act / Assert
            // `false`/`true` are literals, not references — `false == true` is rejected (matches the dashboard).
            await ExpectDiagnostic("false == true", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportUnresolvedBareReference()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("Nonexistent == false", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportUnknownQualifiedService()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NoSuchService.Foo == 1", DaleDiagnostics.DALE041_VisibleWhenUnresolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportMeasuringPointOnlyReference()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportShadowedQualifiedReference()
        {
            // Arrange / Act / Assert
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

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.1")]
        public async Task ReportBareReferenceNamingSiblingService()
        {
            // Arrange / Act / Assert
            // Comp has a bool property 'Point2' whose name also identifies the sibling component service
            // 'Point2'. A BARE ref 'Point2' is ambiguous even though the own service (Point1) has that
            // property — in the flat evaluation context the service object shadows the property.
            var source = @"
using Vion.Dale.Sdk.Core;

public class Comp
{
    [ServiceProperty] public bool Point2 { get; set; }
    [ServiceProperty] [Presentation(VisibleWhen = {|#0:""Point2""|})] public bool X { get; set; }
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
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportUnquotedEnumMemberInPredicate()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("Mode == Eco", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportUnknownEnumMemberInEquality()
        {
            // Arrange / Act / Assert
            // A typo in an enum member must fail CLOSED — a clean build here would permanently hide the row.
            await ExpectDiagnostic("Mode == 'Ecoo'", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportUnknownEnumMemberInList()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("Mode in ['Eco', 'Fastt']", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportNonHomogeneousList()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NumChargingPoints in [1, 'two']", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportBareNonBoolReference()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NumChargingPoints", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportRelationalOperatorOnNonInteger()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("DirectMeasurement > 1", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportLiteralTypeMismatchInEquality()
        {
            // Arrange / Act / Assert
            await ExpectDiagnostic("NumChargingPoints == true", DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportDoubleTypedReference()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-013.3")]
        public async Task ReportWriteOnlyReference()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-013.4")]
        public async Task ResolveQualifiedReferenceToComponentFromReferencedAssembly()
        {
            // Arrange / Act / Assert
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

        private static string Block(string annotatedMember)
        {
            return Scaffold.Replace("{0}", annotatedMember);
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
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(VisibleWhenPredicateAnalyzerTests).Assembly.Location)!,
                                         "..",
                                         "..",
                                         "..",
                                         "Helpers",
                                         "TestAttributeStubs.cs");
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

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.2")]
        public async Task ReportUnresolvedPredicateOnAbstractBlock()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public abstract class BlockBase : LogicBlockBase
{
    [ServiceProperty]
    [Presentation(VisibleWhen = {|#0:""NoSuchProperty""|})]
    public int Gated { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE041_VisibleWhenUnresolved)
                                           .WithLocation(0)
                                           .WithArguments("Gated",
                                                          "NoSuchProperty",
                                                          "service 'BlockBase' has no service property 'NoSuchProperty' (a bare reference must be a property on the same service)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.2")]
        public async Task ReportPredicateNamingPropertyOnlySubclassesDeclare()
        {
            // Arrange / Act / Assert
            // The predicate must resolve where it is written; a property a subclass adds is not on the
            // abstract block's own service.
            var source = @"
using Vion.Dale.Sdk.Core;

public abstract class BlockBase : LogicBlockBase
{
    [ServiceProperty]
    [Presentation(VisibleWhen = {|#0:""Detailed""|})]
    public int Gated { get; set; }
}

public sealed class MyBlock : BlockBase
{
    [ServiceProperty] public bool Detailed { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE041_VisibleWhenUnresolved)
                                           .WithLocation(0)
                                           .WithArguments("Gated",
                                                          "Detailed",
                                                          "service 'BlockBase' has no service property 'Detailed' (a bare reference must be a property on the same service)");
            await AnalyzerTestBase.VerifyAnalyzerAsync<VisibleWhenPredicateAnalyzer>(source, expected);
        }
    }
}