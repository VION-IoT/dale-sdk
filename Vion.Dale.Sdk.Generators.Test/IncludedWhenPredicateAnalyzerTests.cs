using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE043 / DALE044 coverage for <c>IncludedWhenPredicateAnalyzer</c> (RFC 0016). Positive cases
    ///     assert a well-formed gate produces no diagnostic; negative cases assert the right diagnostic id
    ///     fires at the predicate string (the message text is intentionally not pinned).
    /// </summary>
    [TestClass]
    public class IncludedWhenPredicateAnalyzerTests
    {
        private const string Scaffold = @"
using Vion.Dale.Sdk.Core;

public enum Model { Bricco, Moka, Ristretto }

public class Point
{
    [ServiceProperty] public bool Active { get; set; }
}

public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; } = 1;
    [ServiceProperty] [InstantiationParameter] public Model Model { get; init; }
    [ServiceProperty] public int Setting { get; set; }

    {0}
}";

        // ── Positive cases (no diagnostic) ──

        [TestMethod]
        [DataRow("Count >= 2", DisplayName = "relational over a count parameter")]
        [DataRow("Model in ['Moka', 'Ristretto']", DisplayName = "enum membership")]
        [DataRow("Count >= 2 && Model == 'Moka'", DisplayName = "compound count + enum")]
        [DataRow("!(Count == 1)", DisplayName = "negated parenthesized comparison")]
        public async Task ValidGateOnAComponent_NoDiagnostic(string predicate)
        {
            var source = Block($"[IncludedWhen(\"{predicate}\")] public Point Point2 {{ get; }} = new();");
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        public async Task ValidGateOnAContractBinding_NoDiagnostic()
        {
            var source = Block("[ServiceProviderContractBinding] [IncludedWhen(\"Count >= 2\")] public IThing Output { get; private set; }") + "\npublic interface IThing { }";

            // A contract binding is gateable; declare a plain interface property with the binding attribute.
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source);
        }

        [TestMethod]
        public async Task GateAndParameterDeclaredOnABaseClass_ResolveOnTheLeaf_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class Point { [ServiceProperty] public bool Active { get; set; } }
public class BaseStation : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; } = 1;
    [IncludedWhen(""Count >= 2"")] public Point Point2 { get; } = new();
}
public class LeafStation : BaseStation
{
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source);
        }

        // ── Negative cases on a gated component (predicate markup) ──

        [TestMethod]
        public Task PredicateOutsideTheGrammar_ReportsDALE043()
        {
            return ExpectGate("Count >>> 2", DaleDiagnostics.DALE043_IncludedWhenInvalid);
        }

        [TestMethod]
        public Task QualifiedReference_ReportsDALE043()
        {
            return ExpectGate("MyBlock.Count >= 2", DaleDiagnostics.DALE043_IncludedWhenInvalid);
        }

        [TestMethod]
        public Task ReferenceToANonParameterProperty_ReportsDALE043()
        {
            return ExpectGate("Setting >= 2", DaleDiagnostics.DALE043_IncludedWhenInvalid);
        }

        [TestMethod]
        public Task TypeMismatchInPredicate_ReportsDALE044()
        {
            return ExpectGate("Count == 'text'", DaleDiagnostics.DALE044_InstantiationParameterDiscipline);
        }

        [TestMethod]
        public Task UnquotedEnumMember_ReportsDALE044()
        {
            return ExpectGate("Model == Moka", DaleDiagnostics.DALE044_InstantiationParameterDiscipline);
        }

        // ── Negative cases on placement (custom source) ──

        [TestMethod]
        public async Task GateOnAScalarServiceProperty_ReportsDALE043()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [ServiceProperty] [IncludedWhen({|#0:""Count >= 2""|})] public bool X { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        [TestMethod]
        public async Task GateOnAScalarMeasuringPoint_ReportsDALE043()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [ServiceMeasuringPoint] [IncludedWhen({|#0:""Count >= 2""|})] public double Power { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        [TestMethod]
        public async Task GateOnATimerMethod_ReportsDALE043()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [IncludedWhen({|#0:""Count >= 2""|})] [Timer(1)] public void Tick() { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        [TestMethod]
        public async Task GateOnTheBlockClass_ReportsDALE043()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[IncludedWhen({|#0:""Count >= 2""|})]
public class MyBlock : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        [TestMethod]
        public async Task RegatedOverride_ReportsDALE043()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
public class Point { [ServiceProperty] public bool Active { get; set; } }
public class BaseStation : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [IncludedWhen(""Count >= 2"")] public virtual Point Point2 { get; set; } = new();
}
public class LeafStation : BaseStation
{
    [IncludedWhen({|#0:""Count >= 3""|})] public override Point Point2 { get; set; } = new();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        [TestMethod]
        public async Task RegatedNewShadow_ReportsDALE043()
        {
            // A `new` shadow (not an override) that re-declares the gate — exercises the BaseType-chain walk.
            var source = @"
using Vion.Dale.Sdk.Core;
public class Point { [ServiceProperty] public bool Active { get; set; } }
public class BaseStation : LogicBlockBase
{
    [ServiceProperty] [InstantiationParameter] public int Count { get; init; }
    [IncludedWhen(""Count >= 2"")] public Point Point2 { get; } = new();
}
public class LeafStation : BaseStation
{
    [IncludedWhen({|#0:""Count >= 3""|})] public new Point Point2 { get; } = new();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(DaleDiagnostics.DALE043_IncludedWhenInvalid).WithLocation(0));
        }

        // ── Helpers ──

        private static string Block(string annotatedMember)
        {
            return Scaffold.Replace("{0}", annotatedMember);
        }

        private static DiagnosticResult Diag(DiagnosticDescriptor descriptor)
        {
            return new DiagnosticResult(descriptor.Id, DiagnosticSeverity.Error);
        }

        private static async Task ExpectGate(string predicate, DiagnosticDescriptor descriptor)
        {
            var source = Block($"[IncludedWhen({{|#0:\"{predicate}\"|}})] public Point Point2 {{ get; }} = new();");
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, Diag(descriptor).WithLocation(0));
        }
    }
}