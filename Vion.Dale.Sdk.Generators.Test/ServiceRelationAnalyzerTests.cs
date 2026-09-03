using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE045 coverage for <c>ServiceRelationAnalyzer</c>. The ID carries both
    ///     severities, so every case asserts which one it is: errors for a broken declaration, warnings for a
    ///     declaration that is legal but silently emits nothing (or collides across contracts).
    /// </summary>
    [TestClass]
    public class ServiceRelationAnalyzerTests
    {
        /// <summary>
        ///     A well-formed relation-bearing contract plus the generated-interface shape blocks bind against.
        ///     Prepended to most cases so they only have to add the part under test.
        /// </summary>
        private const string ConsumerContractSource = @"
using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.CodeGeneration;

[LogicBlockContract(BetweenInterface = ""IConsumer"", AndInterface = ""IConsumerManager"")]
[ServiceRelation(RelationType = ""LinkedConsumer"", OutwardsInterface = ""IConsumer"")]
public static class ConsumerContract { }

public interface IConsumerSender { }
public interface IConsumerManagerSender { }

[LogicInterface(MatchingInterface = typeof(IConsumerManager), SenderInterface = typeof(IConsumerSender), ContractType = typeof(ConsumerContract))]
public interface IConsumer { }

[LogicInterface(MatchingInterface = typeof(IConsumer), SenderInterface = typeof(IConsumerManagerSender), ContractType = typeof(ConsumerContract))]
public interface IConsumerManager { }
";

        // ── Positive cases (no diagnostic) ──

        [TestMethod]
        public async Task AWellFormedContractDeclaration_NoDiagnostic()
        {
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(ConsumerContractSource);
        }

        [TestMethod]
        public async Task SeveralDistinctRelationTypesOnOneContract_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IMultiSource"", AndInterface = ""IMultiSink"")]
[ServiceRelation(RelationType = ""LinkedAlpha"", OutwardsInterface = ""IMultiSource"")]
[ServiceRelation(RelationType = ""LinkedBeta"", OutwardsInterface = ""IMultiSink"")]
public static class MultiRelationContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source);
        }

        [TestMethod]
        public async Task AServiceBearingComponentEndpoint_NoDiagnostic()
        {
            // A [ServiceProperty] is enough to give the component a node in the cloud graph.
            var source = ConsumerContractSource + @"
public class ChargePoint : IConsumer
{
    [ServiceProperty] public double Limit { get; set; }
}
public class Station : LogicBlockBase
{
    public ChargePoint Point1 { get; } = new ChargePoint();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source);
        }

        [TestMethod]
        public async Task AServiceLessComponentOnANonRelationBearingContract_NoDiagnostic()
        {
            // Same service-less shape, but the contract declares no relation — nothing is lost, nothing to warn about.
            var source = @"
using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.CodeGeneration;

[LogicBlockContract(BetweenInterface = ""IProbeSource"", AndInterface = ""IProbeSink"")]
public static class ProbeContract { }

public interface IProbeSinkSender { }

[LogicInterface(MatchingInterface = typeof(IProbeSink), SenderInterface = typeof(IProbeSinkSender), ContractType = typeof(ProbeContract))]
public interface IProbeSink { }

public class Probe : IProbeSink { }

public class Station : LogicBlockBase
{
    public Probe Sink { get; } = new Probe();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source);
        }

        [TestMethod]
        public async Task AServiceLessTypeThatIsNotAComponentOfALogicBlock_NoDiagnostic()
        {
            // Property-based interface binding only happens on the logic-block class, so a plain holder
            // carrying the same property is not an endpoint and must not warn.
            var source = ConsumerContractSource + @"
public class BareConsumer : IConsumer { }
public class NotABlock
{
    public BareConsumer Consumer { get; } = new BareConsumer();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source);
        }

        [TestMethod]
        public async Task ARelationBearingInterfaceImplementedByTheBlockItself_NoDiagnostic()
        {
            // A class-implemented endpoint always belongs to the root service, which is never service-less.
            var source = ConsumerContractSource + @"
public class Manager : LogicBlockBase, IConsumerManager
{
    [ServiceProperty] public bool Enabled { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source);
        }

        // ── Errors ──

        [TestMethod]
        public async Task RelationOnANonContractClass_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[{|#0:ServiceRelation(RelationType = ""Orphaned"", OutwardsInterface = ""IAnything"")|}]
public static class NotAContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        public async Task OutwardsInterfaceNamingNeitherSide_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IBadSource"", AndInterface = ""IBadSink"")]
[{|#0:ServiceRelation(RelationType = ""LinkedBad"", OutwardsInterface = ""INotOnThisContract"")|}]
public static class InvalidOutwardsContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        public async Task DuplicateRelationTypeOnOneContract_ReportsErrorOnTheDuplicate()
        {
            // Only the second declaration is reported — the first is the legitimate one.
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IDupSource"", AndInterface = ""IDupSink"")]
[ServiceRelation(RelationType = ""LinkedTwice"", OutwardsInterface = ""IDupSource"")]
[{|#0:ServiceRelation(RelationType = ""LinkedTwice"", OutwardsInterface = ""IDupSink"")|}]
public static class DuplicateContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        public async Task EmptyRelationType_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IEmptySource"", AndInterface = ""IEmptySink"")]
[{|#0:ServiceRelation(RelationType = """", OutwardsInterface = ""IEmptySource"")|}]
public static class EmptyRelationTypeContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        public async Task WhitespaceOutwardsInterface_ReportsError()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IBlankSource"", AndInterface = ""IBlankSink"")]
[{|#0:ServiceRelation(RelationType = ""LinkedBlank"", OutwardsInterface = ""   "")|}]
public static class BlankOutwardsContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        // ── Warnings ──

        [TestMethod]
        public async Task AServiceLessComponentOnARelationBearingContract_ReportsWarning()
        {
            var source = ConsumerContractSource + @"
public class BareConsumer : IConsumer { }
public class Station : LogicBlockBase
{
    public BareConsumer {|#0:Consumer|} { get; } = new BareConsumer();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Warning().WithLocation(0));
        }

        [TestMethod]
        public async Task TheSameRelationTypeOnTwoContracts_ReportsWarningOnBoth()
        {
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IV1Source"", AndInterface = ""IV1Sink"")]
[{|#0:ServiceRelation(RelationType = ""LinkedShared"", OutwardsInterface = ""IV1Source"")|}]
public static class ContractV1 { }

[LogicBlockContract(BetweenInterface = ""IV2Source"", AndInterface = ""IV2Sink"")]
[{|#1:ServiceRelation(RelationType = ""LinkedShared"", OutwardsInterface = ""IV2Source"")|}]
public static class ContractV2 { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Warning().WithLocation(0), Warning().WithLocation(1));
        }

        [TestMethod]
        public async Task AServiceLessComponentNamingAnUnresolvedContractInterface_ReportsWarning()
        {
            // The shape that actually reaches the analyzer in a real build: a contract's interfaces are
            // emitted by LogicClassGenerator, whose output is NOT part of the compilation analyzers see here,
            // so 'IGenSink' does not resolve to a symbol at all. The endpoint must still be recognised — by
            // name, off the contract's own AndInterface string. Without this the warning is dead in every
            // same-library case.
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
[ServiceRelation(RelationType = ""LinkedGenerated"", OutwardsInterface = ""IGenSink"")]
public static class GeneratedContract { }

public class BareSink : {|#1:IGenSink|} { }

public class Station : LogicBlockBase
{
    public BareSink {|#0:Sink|} { get; } = new BareSink();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source,
                                                                                Warning().WithLocation(0),
                                                                                DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSink"));
        }

        [TestMethod]
        public async Task AServiceBearingComponentNamingAnUnresolvedContractInterface_NoWarning()
        {
            // Same unresolved-interface shape, but the component is a service — the by-name path must not
            // over-report.
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IGenSource"", AndInterface = ""IGenSink"")]
[ServiceRelation(RelationType = ""LinkedGenerated"", OutwardsInterface = ""IGenSink"")]
public static class GeneratedContract { }

public class ServiceSink : {|#1:IGenSink|}
{
    [ServiceProperty] public double Limit { get; set; }
}

public class Station : LogicBlockBase
{
    public ServiceSink Sink { get; } = new ServiceSink();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, DiagnosticResult.CompilerError("CS0246").WithLocation(1).WithArguments("IGenSink"));
        }

        // ── Composed behaviour ──

        [TestMethod]
        public async Task ABrokenDeclarationIsNotCountedForTheCrossContractCollision()
        {
            // The duplicate on one contract is already an error; it must not also inflate the cross-contract
            // tally into a second, redundant warning about a contract that legitimately declares it once.
            var source = @"
using Vion.Dale.Sdk.Core;
[LogicBlockContract(BetweenInterface = ""IDupSource"", AndInterface = ""IDupSink"")]
[ServiceRelation(RelationType = ""LinkedTwice"", OutwardsInterface = ""IDupSource"")]
[{|#0:ServiceRelation(RelationType = ""LinkedTwice"", OutwardsInterface = ""IDupSink"")|}]
public static class DuplicateContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        public async Task AnErrorAndAWarningCoexistOnOneCompilation()
        {
            // A broken contract next to a legal-but-silent component endpoint: both fire, at their own
            // severities, and neither suppresses the other.
            var source = ConsumerContractSource + @"
public class BareConsumer : IConsumer { }
public class Station : LogicBlockBase
{
    public BareConsumer {|#1:Consumer|} { get; } = new BareConsumer();
}

[LogicBlockContract(BetweenInterface = ""IBadSource"", AndInterface = ""IBadSink"")]
[{|#0:ServiceRelation(RelationType = ""LinkedBad"", OutwardsInterface = ""INotOnThisContract"")|}]
public static class InvalidOutwardsContract { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Error().WithLocation(0), Warning().WithLocation(1));
        }

        [TestMethod]
        public async Task AComponentImplementingBothSidesOfARelationBearingContract_ReportsOneWarning()
        {
            // Two relation-bearing endpoints on the same service-less component still produce a single
            // finding — the fix is per property, not per interface.
            var source = ConsumerContractSource + @"
public class DualRole : IConsumer, IConsumerManager { }
public class Station : LogicBlockBase
{
    public DualRole {|#0:Both|} { get; } = new DualRole();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceRelationAnalyzer>(source, Warning().WithLocation(0));
        }

        // ── Helpers ──

        private static DiagnosticResult Error()
        {
            return new DiagnosticResult(DaleDiagnostics.DALE045_ServiceRelationDiscipline.Id, DiagnosticSeverity.Error);
        }

        private static DiagnosticResult Warning()
        {
            return new DiagnosticResult(DaleDiagnostics.DALE045_ServiceRelationDiscipline.Id, DiagnosticSeverity.Warning);
        }
    }
}