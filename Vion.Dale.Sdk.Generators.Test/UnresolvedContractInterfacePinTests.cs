using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     The real-compilation pin required by
    ///     <see href="../../docs/sdk-surface-conventions.md">sdk-surface-conventions</see> § 5 and
    ///     <see href="../../docs/testing-conventions.md">testing-conventions</see> § 3, for the analyzers
    ///     that key off a contract interface other than <c>ServiceRelationAnalyzer</c> (whose own suite
    ///     carries the worked example).
    ///     <para>
    ///         Every logic-block project references Metalama, which replaces the compiler task, and in that
    ///         pipeline an interface <c>LogicClassGenerator</c> emits resolves to an <b>error type</b>:
    ///         <c>AllInterfaces</c> simply does not contain it. A stub interface that resolves cleanly does
    ///         not reproduce that, so each test here expects <c>CS0246</c> alongside — or instead of — the
    ///         Dale diagnostic, and asserts what the analyzer does when the interface it keys off is gone.
    ///     </para>
    ///     <para>
    ///         The proxy is harsher than the real thing in one way, and that bounds what these tests can
    ///         claim: here the interface does not exist at all, while in a Metalama build it exists for the
    ///         compiler and is an error type only for the analyzer. So no fixture here can exercise the
    ///         remedy an author would reach for — <c>[LogicBlockInterfaceBinding(typeof(TheInterface))]</c>
    ///         — because the <c>typeof</c> is the very thing that will not resolve.
    ///     </para>
    /// </summary>
    [TestClass]
    public class UnresolvedContractInterfacePinTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.4")]
        public async Task StaySilentOnUnresolvedContractPropertyType()
        {
            // Arrange / Act / Assert
            // DALE001 resolves the contract by SYMBOL, through [ServiceProviderContractType] on the
            // property's own type or its interfaces. Those interfaces are author-written, never generated,
            // so an unresolved one is an ordinary compile error and the analyzer correctly adds nothing.
            var source = @"
public class MyBlock
{
    public {|#0:IMissingContract|} Input { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source,
                                                                                 DiagnosticResult.CompilerError("CS0246").WithLocation(0).WithArguments("IMissingContract"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.6")]
        public async Task MatchContractRoleNamesByNameAgainstUnresolvedInterface()
        {
            // Arrange / Act / Assert
            // DALE009 and DALE010 compare the attribute's STRINGS, never a symbol, so they hold when the
            // interfaces they name resolve to nothing — which is every real build of a generated contract.
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
public class GeneratedContract
{
    [Command(From = ""IStranger"", To = ""IGenSink"")]
    public readonly record struct {|#0:Nudge|}(int Amount);
}

public class Endpoint : {|#1:IGenSource|} { }";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE010_MessageFromToMismatch)
                                           .WithLocation(0)
                                           .WithArguments("Nudge", "From", "IStranger", "IGenSource", "IGenSink");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source,
                                                                                expected,
                                                                                DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSource"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-014.4")]
        public async Task StaySilentOnGateOverPropertyWithUnresolvedInterface()
        {
            // Arrange / Act / Assert
            // The shape every consumer writes: a component class implementing a generated contract
            // interface, held by a gated property. `IGenSink` does not resolve to a symbol, so the
            // by-symbol half finds nothing and the by-name half — the base-list identifier matched against
            // the contract's own AndInterface string — is the only thing that can see the binding.
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
public class GeneratedContract { }

public class Component : {|#1:IGenSink|} { }

public class MyBlock : LogicBlockBase
{
    [InstantiationParameter][ServiceProperty] public bool UseBackup { get; set; }

    [IncludedWhen(""UseBackup"")] public Component Backup { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSink"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-014.4")]
        public async Task StaySilentOnGateOverPropertyInheritingUnresolvedInterfaceFromBaseClass()
        {
            // Arrange / Act / Assert
            // DeclarativeInterfaceBinder binds on Type.GetInterfaces(), which is transitive, so a component
            // that picks its endpoint up from a base class binds exactly like one declaring it directly.
            // Reading only the component's own base list would refuse this and bind it anyway.
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
public class GeneratedContract { }

public class ComponentBase : {|#1:IGenSink|} { }

public class Component : ComponentBase { }

public class MyBlock : LogicBlockBase
{
    [InstantiationParameter][ServiceProperty] public bool UseBackup { get; set; }

    [IncludedWhen(""UseBackup"")] public Component Backup { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSink"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-014.4")]
        public async Task StaySilentOnGateOverPropertyImplementingInterfaceExtendingUnresolvedInterface()
        {
            // Arrange / Act / Assert
            // The other half of the same transitivity: the endpoint arrives through an interface the
            // component implements, not through the component's own base list. That intermediate interface
            // resolves, so its base list is where the generated name is written.
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
public class GeneratedContract { }

public interface IStationEndpoint : {|#1:IGenSink|} { }

public class Component : IStationEndpoint { }

public class MyBlock : LogicBlockBase
{
    [InstantiationParameter][ServiceProperty] public bool UseBackup { get; set; }

    [IncludedWhen(""UseBackup"")] public Component Backup { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source, DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSink"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-014.4")]
        [TestProperty("spec", "AC-GATE-011.3")]
        public async Task ReportGateOnPropertyNamingUnresolvedNonContractInterface()
        {
            // Arrange / Act / Assert
            // The boundary of the by-name half, and the direction that fails silently: an unresolved
            // name that no [LogicBlockContract] in this compilation declares as a role interface is not a
            // binding, so the gate stays ungateable. Matching any unresolved name would admit declarations
            // that compile here and throw at bind.
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
public class GeneratedContract { }

public class Component : {|#1:IStranger|} { }

public class MyBlock : LogicBlockBase
{
    [InstantiationParameter][ServiceProperty] public bool UseBackup { get; set; }

    [IncludedWhen({|#0:""UseBackup""|})] public Component Backup { get; private set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE043_IncludedWhenInvalid)
                                           .WithLocation(0)
                                           .WithArguments("Backup",
                                                          "UseBackup",
                                                          "this member is not gateable — only a property-based interface binding, a contract binding, or a service-bearing " +
                                                          "component can carry [IncludedWhen]. A scalar service property/measuring point keeps publishing; use " +
                                                          "[Presentation(VisibleWhen = ...)] for display relevance.");
            await AnalyzerTestBase.VerifyAnalyzerAsync<IncludedWhenPredicateAnalyzer>(source,
                                                                                      expected,
                                                                                      DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IStranger"));
        }
    }
}