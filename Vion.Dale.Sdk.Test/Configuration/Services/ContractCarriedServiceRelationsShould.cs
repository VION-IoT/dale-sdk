using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Services
{
    // ── RFC 0019 contract-carried service-relation fixtures ─────────────────────────────────────────

    /// <summary>A cascade: one universal block plays both parent and child, so both halves land on one class.</summary>
    [LogicBlockContract(BetweenInterface = "ICascadeParent", AndInterface = "ICascadeChild")]
    [ServiceRelation(RelationType = "LinkedParentManager", OutwardsInterface = "ICascadeChild")]
    public static class CascadeContract
    {
        [StateUpdate(From = "ICascadeParent", To = "ICascadeChild")]
        public readonly record struct LimitChanged(double Watts);
    }

    /// <summary>The device contract carried by nested components — the ChargePoint shape.</summary>
    [LogicBlockContract(BetweenInterface = "IConsumer", AndInterface = "IConsumerManager")]
    [ServiceRelation(RelationType = "LinkedConsumer", OutwardsInterface = "IConsumer")]
    public static class ConsumerContract
    {
        [StateUpdate(From = "IConsumerManager", To = "IConsumer")]
        public readonly record struct ConsumerLimitChanged(double Watts);
    }

    /// <summary>Two relation types on one contract, deliberately naming opposite outwards sides.</summary>
    [LogicBlockContract(BetweenInterface = "IMultiSource", AndInterface = "IMultiSink")]
    [ServiceRelation(RelationType = "LinkedAlpha", OutwardsInterface = "IMultiSource")]
    [ServiceRelation(RelationType = "LinkedBeta", OutwardsInterface = "IMultiSink")]
    public static class MultiRelationContract
    {
        [StateUpdate(From = "IMultiSource", To = "IMultiSink")]
        public readonly record struct Tick(int N);
    }

    // The next two fixtures are exactly what DALE045 reports as errors — they exist to prove the bind-time
    // guards still fire for anyone who suppresses the analyzer, so the analyzer is suppressed over them here.
#pragma warning disable DALE045

    /// <summary>A contract whose <c>OutwardsInterface</c> names neither of its two sides.</summary>
    [LogicBlockContract(BetweenInterface = "IBadSource", AndInterface = "IBadSink")]
    [ServiceRelation(RelationType = "LinkedBad", OutwardsInterface = "INotOnThisContract")]
    public static class InvalidOutwardsContract
    {
        [StateUpdate(From = "IBadSource", To = "IBadSink")]
        public readonly record struct Nudge(int N);
    }

    /// <summary>
    ///     A <c>[ServiceRelation]</c> carrier that is not a <c>[LogicBlockContract]</c>. Unreachable through
    ///     the generator (only a contract yields <c>[LogicInterface(ContractType = …)]</c>), so the endpoint
    ///     below is hand-written to exercise the bind-time guard.
    /// </summary>
    [ServiceRelation(RelationType = "Orphaned", OutwardsInterface = "IOrphanEndpoint")]
    public static class NotAContract
    {
    }

#pragma warning restore DALE045

    public interface IOrphanSenderInterface : ILogicSenderInterface
    {
    }

    [LogicInterface(MatchingInterface = typeof(IOrphanEndpoint), SenderInterface = typeof(IOrphanSenderInterface), ContractType = typeof(NotAContract))]
    public interface IOrphanEndpoint : ILogicHandlerInterface
    {
    }

    /// <summary>Mirrors the generated sender-interface shape (conventional constructor) for <see cref="IOrphanEndpoint" />.</summary>
    public sealed class OrphanSenderInterface : LogicSenderInterfaceBase, IOrphanSenderInterface
    {
        public OrphanSenderInterface(string identifier, IOrphanEndpoint implementation, Func<LogicBlockId> logicBlockId, IActorContext actorContext, ILogger logger) :
            base(identifier,
                 typeof(IOrphanEndpoint),
                 typeof(IOrphanEndpoint),
                 logicBlockId,
                 actorContext,
                 logger)
        {
        }

        public override void HandleMessage(IFunctionInterfaceMessage functionInterfaceMessage)
        {
        }
    }

    /// <summary>A service-bearing consumer component — it has a node in the cloud graph, so it owns halves.</summary>
    public sealed class ChargePoint : IConsumer
    {
        [ServiceProperty(Unit = "W")]
        public double Limit { get; private set; }

        public void HandleStateUpdate(InterfaceId functionId, ConsumerContract.ConsumerLimitChanged response)
        {
            Limit = response.Watts;
        }
    }

    /// <summary>The same endpoint on a component with no service surface — wires normally, owns no half.</summary>
    public sealed class BareConsumer : IConsumer
    {
        public double Limit { get; private set; }

        public void HandleStateUpdate(InterfaceId functionId, ConsumerContract.ConsumerLimitChanged response)
        {
            Limit = response.Watts;
        }
    }

    /// <summary>Dual-role: both cascade sides on one class, plus both sides of the two-relation contract.</summary>
    public sealed class CascadeBlock : LogicBlockBase, ICascadeParent, ICascadeChild, IMultiSource
    {
        [ServiceProperty(Unit = "W")]
        public double Limit { get; private set; }

        public CascadeBlock() : base(new Mock<ILogger>().Object)
        {
        }

        public void HandleStateUpdate(InterfaceId functionId, CascadeContract.LimitChanged response)
        {
            Limit = response.Watts;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>Class-level identifier override on a class-implemented endpoint.</summary>
    [LogicBlockInterfaceBinding(typeof(IConsumerManager), Identifier = "Fleet")]
    public sealed class ConsumerManagerBlock : LogicBlockBase, IConsumerManager
    {
        [ServiceProperty]
        public bool Enabled { get; private set; }

        public ConsumerManagerBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>The multi-component shape: two peer components, a renamed endpoint, and a service-less one.</summary>
    public sealed class MultiPointBlock : LogicBlockBase
    {
        public ChargePoint ChargePoint1 { get; } = new();

        public ChargePoint ChargePoint2 { get; } = new();

        [LogicBlockInterfaceBinding(typeof(IConsumer), Identifier = "PrimaryConsumer")]
        public ChargePoint Renamed { get; } = new();

        // Deliberately the shape DALE045 warns about — this fixture asserts the runtime's half of that
        // contract (the endpoint wires, no half is emitted, nothing falls back to the root service).
#pragma warning disable DALE045
        public BareConsumer Bare { get; } = new();
#pragma warning restore DALE045

        public MultiPointBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>An RFC 0016-gated component endpoint.</summary>
    public sealed class GatedConsumerBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public ChargePoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public ChargePoint Point2 { get; } = new();

        public GatedConsumerBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    public sealed class InvalidOutwardsBlock : LogicBlockBase, IBadSource
    {
        [ServiceProperty]
        public bool Enabled { get; private set; }

        public InvalidOutwardsBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    public sealed class OrphanCarrierBlock : LogicBlockBase, IOrphanEndpoint
    {
        [ServiceProperty]
        public bool Enabled { get; private set; }

        public OrphanCarrierBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     RFC 0019 — relations are declared once on the <c>[LogicBlockContract]</c> and derived per bound
    ///     interface endpoint, on the service that owns that endpoint. These tests pin the derivation rules
    ///     and the emitted <c>inwardRelations</c> / <c>outwardRelations</c> shape.
    /// </summary>
    [TestClass]
    public sealed class ContractCarriedServiceRelationsShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        // ── Direction + the dual-role class ───────────────────────────────────────────────────────

        [TestMethod]
        public void DeriveBothHalvesOfOneRelationOnADualRoleClass()
        {
            var root = RootService(new CascadeBlock());

            // One declaration, one class implementing both contract sides → exactly one half per side, with
            // the endpoints' own identifiers. Distinct identifiers are what keep cloud-api's per-side
            // pre-filter from cross-pairing the block with itself.
            var outward = root.OutwardRelations.Single(r => r.RelationType == "LinkedParentManager");
            var inward = root.InwardRelations.Single(r => r.RelationType == "LinkedParentManager");

            Assert.AreEqual("ICascadeChild", outward.InterfaceIdentifier, "OutwardsInterface names the subordinate side.");
            Assert.AreEqual("ICascadeParent", inward.InterfaceIdentifier, "The other contract interface is the aggregating side.");
        }

        [TestMethod]
        public void EmitTheEndpointsLogicInterfaceTypeAndAnEmptyAnnotationBag()
        {
            var outward = RootService(new CascadeBlock()).OutwardRelations.Single(r => r.RelationType == "LinkedParentManager");

            Assert.AreEqual(typeof(ICascadeChild).FullName, outward.InterfaceTypeFullName);
            Assert.IsEmpty(outward.Annotations, "Relation-level labels were dropped in RFC 0019 §4.5; the field stays for additive future use.");
        }

        [TestMethod]
        public void DeriveOneHalfPerDeclarationWhenAContractCarriesSeveral()
        {
            // CascadeBlock implements IMultiSource only. LinkedAlpha names it as the outwards side;
            // LinkedBeta names the other side, so the same endpoint is the inwards half of that relation.
            var root = RootService(new CascadeBlock());

            Assert.AreEqual("IMultiSource", root.OutwardRelations.Single(r => r.RelationType == "LinkedAlpha").InterfaceIdentifier);
            Assert.AreEqual("IMultiSource", root.InwardRelations.Single(r => r.RelationType == "LinkedBeta").InterfaceIdentifier);
        }

        [TestMethod]
        public void CarryAClassLevelIdentifierOverrideIntoTheHalf()
        {
            var root = RootService(new ConsumerManagerBlock());

            // The half is registered by the same code path that minted the endpoint identifier, so an
            // override can no longer make the two diverge.
            Assert.AreEqual("Fleet", root.InwardRelations.Single(r => r.RelationType == "LinkedConsumer").InterfaceIdentifier);
        }

        // ── Component endpoints ───────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AttachComponentHalvesToTheComponentServiceWithItsWiringIdentifier()
        {
            var services = ServicesOf(new MultiPointBlock());

            // Each component property is its own service and carries exactly its own endpoint's half — the
            // shape that previously could not be expressed at all (relation identifier vs wiring identifier).
            Assert.AreEqual("ChargePoint1_IConsumer", OnlyOutward(services, "ChargePoint1").InterfaceIdentifier);
            Assert.AreEqual("ChargePoint2_IConsumer", OnlyOutward(services, "ChargePoint2").InterfaceIdentifier);

            Assert.AreEqual("LinkedConsumer", OnlyOutward(services, "ChargePoint1").RelationType);
        }

        [TestMethod]
        public void CarryAPropertyLevelIdentifierOverrideIntoTheHalf()
        {
            var services = ServicesOf(new MultiPointBlock());

            Assert.AreEqual("PrimaryConsumer", OnlyOutward(services, "Renamed").InterfaceIdentifier);
        }

        [TestMethod]
        public void EmitNoHalfForANonServiceBearingComponent()
        {
            var block = new MultiPointBlock();
            var services = ServicesOf(block);

            // No service surface → no node in the cloud graph → nothing correct to anchor the edge to. A
            // root-service fallback would collapse the two ChargePoint edges and can crash the activation
            // projection on PK-identical rows (RFC 0019 §4.2). DALE045 warns at the property instead.
            Assert.IsFalse(services.ContainsKey("Bare"), "A service-less component produces no service at all.");

            var root = services[nameof(MultiPointBlock)];
            Assert.AreEqual(0, root.OutwardRelations.Count + root.InwardRelations.Count, "The skipped half must not fall back onto the root service.");

            // The endpoint itself still binds and wires normally.
            Assert.Contains("Bare_IConsumer", BoundInterfaceIdentifiers(block));
        }

        // ── Emitted shape (golden pin) ────────────────────────────────────────────────────────────

        [TestMethod]
        public void PinTheEmittedRelationShapePerService()
        {
            var services = ServicesOf(new MultiPointBlock());

            var expected = new[]
                           {
                               "ChargePoint1: out LinkedConsumer|ChargePoint1_IConsumer|" + typeof(IConsumer).FullName + "|{}",
                               "ChargePoint2: out LinkedConsumer|ChargePoint2_IConsumer|" + typeof(IConsumer).FullName + "|{}",
                               "MultiPointBlock: (none)",
                               "Renamed: out LinkedConsumer|PrimaryConsumer|" + typeof(IConsumer).FullName + "|{}",
                           };

            CollectionAssert.AreEqual(expected, services.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => Describe(s.Key, s.Value)).ToArray());
        }

        [TestMethod]
        public void PinTheEmittedRelationShapeForADualRoleClass()
        {
            var services = ServicesOf(new CascadeBlock());

            var expected = new[]
                           {
                               "CascadeBlock: in LinkedBeta|IMultiSource|" + typeof(IMultiSource).FullName + "|{}, " + "in LinkedParentManager|ICascadeParent|" +
                               typeof(ICascadeParent).FullName + "|{}, " + "out LinkedAlpha|IMultiSource|" + typeof(IMultiSource).FullName + "|{}, " +
                               "out LinkedParentManager|ICascadeChild|" + typeof(ICascadeChild).FullName + "|{}",
                           };

            CollectionAssert.AreEqual(expected, services.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => Describe(s.Key, s.Value)).ToArray());
        }

        // ── RFC 0016 gating interplay (§5) ────────────────────────────────────────────────────────

        [TestMethod]
        public void EmitGatedComponentHalvesInDefinitionModeAndOmitThemWhenGatedOutLive()
        {
            // Definition mode binds the full set, so the definition JSON always carries both halves.
            var definition = ServicesOf(new GatedConsumerBlock());
            Assert.AreEqual("Point1_IConsumer", OnlyOutward(definition, "Point1").InterfaceIdentifier);
            Assert.AreEqual("Point2_IConsumer", OnlyOutward(definition, "Point2").InterfaceIdentifier);

            // Live mode: a gated-out endpoint never binds, so no half is registered — "no endpoint, no
            // wiring, no relation".
            var live = LiveRelations(new GatedConsumerBlock { PointCount = 1 });
            Assert.IsTrue(live.ContainsKey("Point1"));
            Assert.IsFalse(live.ContainsKey("Point2"), "A gated-out endpoint registers no relation half in Live mode.");

            Assert.IsTrue(LiveRelations(new GatedConsumerBlock { PointCount = 2 }).ContainsKey("Point2"));
        }

        // ── Bind-time validation (§4.6) ───────────────────────────────────────────────────────────

        [TestMethod]
        public void ThrowWhenOutwardsInterfaceNamesNeitherContractSide()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(new InvalidOutwardsBlock(), _serviceProvider));

            Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
            StringAssert.Contains(exception.InnerException!.Message, "OutwardsInterface");
        }

        [TestMethod]
        public void ThrowWhenTheRelationCarrierIsNotALogicBlockContract()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(new OrphanCarrierBlock(), _serviceProvider));

            Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
            StringAssert.Contains(exception.InnerException!.Message, "[LogicBlockContract]");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        private Dictionary<string, LogicBlockIntrospectionResult.ServiceInfo> ServicesOf(LogicBlockBase block)
        {
            return LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider).Services.ToDictionary(s => s.Identifier, s => s, StringComparer.Ordinal);
        }

        private LogicBlockIntrospectionResult.ServiceInfo RootService(LogicBlockBase block)
        {
            return ServicesOf(block)[block.GetType().Name];
        }

        private static LogicBlockIntrospectionResult.ServiceRelationInfo OnlyOutward(Dictionary<string, LogicBlockIntrospectionResult.ServiceInfo> services,
                                                                                     string serviceIdentifier)
        {
            return services[serviceIdentifier].OutwardRelations.Single();
        }

        private static string Describe(string serviceIdentifier, LogicBlockIntrospectionResult.ServiceInfo service)
        {
            var halves = service.InwardRelations
                                .Select(r => "in " + Describe(r))
                                .Concat(service.OutwardRelations.Select(r => "out " + Describe(r)))
                                .OrderBy(s => s, StringComparer.Ordinal);

            var joined = string.Join(", ", halves);
            return $"{serviceIdentifier}: {(joined.Length == 0 ? "(none)" : joined)}";
        }

        private static string Describe(LogicBlockIntrospectionResult.ServiceRelationInfo relation)
        {
            return $"{relation.RelationType}|{relation.InterfaceIdentifier}|{relation.InterfaceTypeFullName}|{{{string.Join(",", relation.Annotations.Keys)}}}";
        }

        /// <summary>Runs the real Live-mode binders and returns the relation halves per owning service.</summary>
        private Dictionary<string, IReadOnlyList<ServiceRelationInfo>> LiveRelations(LogicBlockBase block)
        {
            DriveInitialize(block);

            var binder = (ServiceBinder)typeof(LogicBlockBase).GetField("_serviceBinder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(block)!;
            return binder.GetAllServiceRelations().ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        }

        private HashSet<string> BoundInterfaceIdentifiers(LogicBlockBase block)
        {
            DriveInitialize(block);

            var interfaces = (System.Collections.IDictionary)typeof(LogicBlockBase).GetField("_interfaces", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(block)!;
            return interfaces.Keys.Cast<string>().ToHashSet(StringComparer.Ordinal);
        }

        private void DriveInitialize(LogicBlockBase block)
        {
            var initialize = new InitializeLogicBlock("cfg",
                                                      block.GetType().Name,
                                                      new Dictionary<string, ServiceIdentifier>(),
                                                      new Dictionary<string, LogicBlockContractId>(),
                                                      _serviceProvider);

            block.HandleMessageAsync(initialize, new Mock<IActorContext>().Object).GetAwaiter().GetResult();
        }
    }
}