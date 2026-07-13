using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration
{
    // ── RFC 0016 config-time structural gating fixtures ──────────────────────────────────────────────

    public enum StationModel
    {
        Bricco,

        Moka,

        Ristretto,

        Cappuccino,
    }

    /// <summary>A service-bearing component whose whole existence is gated by the station parameter.</summary>
    public sealed class GatedPoint
    {
        [ServiceProperty(Title = "Aktiv")]
        public bool Active { get; set; }

        [ServiceMeasuringPoint(Unit = "kW")]
        public double Power { get; private set; }

        [Persistent]
        public double Energy { get; set; }
    }

    public sealed class GatedCountBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public GatedPoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public GatedPoint Point2 { get; } = new();

        [IncludedWhen("PointCount >= 3")]
        public GatedPoint Point3 { get; } = new();

        public GatedCountBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    public sealed class GatedEnumBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Modell")]
        [InstantiationParameter]
        public StationModel Model { get; init; } = StationModel.Bricco;

        public GatedPoint Point1 { get; } = new();

        // References the enum by member-name membership — the context-encoding path the shared vector
        // cannot exercise (the enum must arrive as "Ristretto", not (int)2 or "2").
        [IncludedWhen("Model in ['Moka', 'Ristretto', 'Cappuccino']")]
        public GatedPoint Point2 { get; } = new();

        public GatedEnumBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    public sealed class GatedContractBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        // A gated contract property: null when excluded (the binder is what constructs it) — the documented hazard.
        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 2 aktiv")]
        [IncludedWhen("PointCount >= 2")]
        public IDigitalOutput? Point2Output { get; private set; }

        public GatedContractBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    // An interface-only component: it implements a generated [LogicInterface] but declares NO service
    // members, so it is bound by the interface binder and is ABSENT from the service-property / measuring-
    // point binding keys — the shape the persistence included-set must resolve via the gate, not binder keys.
    [LogicBlockContract(BetweenInterface = "IProbeSource", AndInterface = "IProbeSink", Direction = ContractDirection.Bidirectional)]
    public static class ProbeLink
    {
        [RequestResponse(From = "IProbeSource", To = "IProbeSink", ResponseType = typeof(Ack))]
        public readonly record struct Poll(int N);

        public readonly record struct Ack(int N);
    }

    public sealed class InterfaceOnlyProbe : IProbeSink
    {
        [Persistent]
        public double Energy { get; set; }

        public ProbeLink.Ack HandleRequest(ProbeLink.Poll request)
        {
            return new ProbeLink.Ack(request.N);
        }
    }

    public sealed class GatedInterfaceOnlyBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Anzahl", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int Count { get; init; } = 1;

        [LogicBlockInterfaceBinding(typeof(IProbeSink))]
        [IncludedWhen("Count >= 2")]
        public InterfaceOnlyProbe Probe { get; } = new();

        public GatedInterfaceOnlyBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    public sealed class GatedNullParamBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Region")]
        [InstantiationParameter]
        public string? Region { get; init; } // null default → a gate over it fails closed at bind

        [IncludedWhen("Region == 'EU'")]
        public GatedPoint Point2 { get; } = new();

        public GatedNullParamBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }
    }

    [TestClass]
    public sealed class ConfigTimeStructuralGatingShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        // ── Live-mode binder gating ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void BindOnlyIncludedComponentServices_ForACountParameter()
        {
            var two = BindLiveServiceIds(new GatedCountBlock { PointCount = 2 });
            Assert.Contains("Point1", two);
            Assert.Contains("Point2", two);
            Assert.DoesNotContain("Point3", two); // gated out at PointCount = 2

            Assert.Contains("Point3", BindLiveServiceIds(new GatedCountBlock { PointCount = 3 }));

            var one = BindLiveServiceIds(new GatedCountBlock { PointCount = 1 });
            Assert.DoesNotContain("Point2", one);
            Assert.DoesNotContain("Point3", one);
        }

        // The load-bearing enum test: gating resolves against a JSON member-name string built via
        // PropertyValueCodec.ClrToJson — a context-construction bug (int cast / ToString) would silently
        // mis-resolve here even though the shared vector passes.
        [TestMethod]
        public void ResolveAGatedEnumViaTheJsonMemberNameContext()
        {
            Assert.Contains("Point2", BindLiveServiceIds(new GatedEnumBlock { Model = StationModel.Ristretto }));
            Assert.Contains("Point2", BindLiveServiceIds(new GatedEnumBlock { Model = StationModel.Cappuccino }));

            // Bricco is not in the membership list — Point2 is gated out.
            Assert.DoesNotContain("Point2", BindLiveServiceIds(new GatedEnumBlock { Model = StationModel.Bricco }));
        }

        [TestMethod]
        public void FailClosedAtBindWhenAGateEvaluatesAgainstANullParameter()
        {
            // A gate over a null parameter value is a hard config error (fail-closed) — the strict evaluator
            // throws at bind, which propagates out of Configure so the block reports unhealthy.
            var block = new GatedNullParamBlock();
            var binder = new ServiceBinder();
            var context = InclusionGate.BuildParameterContext(block);

            Assert.Throws<PredicateEvaluationException>(() => DeclarativeServiceBinder.BindServicesFromAttributes(block, binder, BindingMode.Live, context));
        }

        [TestMethod]
        public void LeaveAGatedOutContractPropertyNull()
        {
            var block = new GatedContractBlock { PointCount = 1 };
            var factory = new Mock<IContractFactory>(MockBehavior.Strict);

            DeclarativeContractBinder.BindContractsFromAttributes(block, factory.Object, BindingMode.Live, InclusionGate.BuildParameterContext(block));

            Assert.IsNull(block.Point2Output, "An excluded contract property is never constructed — it stays null.");
            factory.Verify(f => f.Create(It.IsAny<Type>(), It.IsAny<string>()), Times.Never);
        }

        // ── Definition-mode introspection emission ────────────────────────────────────────────────

        [TestMethod]
        public void EmitTheFullMemberSetWithPredicatesInDefinitionMode()
        {
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new GatedCountBlock(), _serviceProvider);
            var serviceIds = result.Services.Select(s => s.Identifier).ToHashSet(StringComparer.Ordinal);

            // Full set regardless of the default instance's PointCount (= 1).
            Assert.Contains("Point1", serviceIds);
            Assert.Contains("Point2", serviceIds);
            Assert.Contains("Point3", serviceIds);

            Assert.AreEqual("PointCount >= 2", result.Services.Single(s => s.Identifier == "Point2").IncludedWhen);
            Assert.AreEqual("PointCount >= 3", result.Services.Single(s => s.Identifier == "Point3").IncludedWhen);
            Assert.IsNull(result.Services.Single(s => s.Identifier == "Point1").IncludedWhen);
        }

        [TestMethod]
        public void EmitTheParameterRuntimeMarkerDefaultAndReadOnly()
        {
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new GatedCountBlock(), _serviceProvider);
            var pointCount = result.Services.Single(s => s.Identifier == nameof(GatedCountBlock)).Properties.Single(p => p.Identifier == nameof(GatedCountBlock.PointCount));

            Assert.IsNotNull(pointCount.Runtime);
            Assert.IsTrue(pointCount.Runtime!["instantiationParameter"]!.GetValue<bool>());
            Assert.AreEqual(1L, pointCount.Runtime["default"]!.GetValue<long>()); // int rides the wire long-backed
            Assert.IsTrue(pointCount.Schema!["readOnly"]!.GetValue<bool>(), "A parameter is forced wire-read-only.");
        }

        [TestMethod]
        public void EmitTheEnumParameterDefaultAsItsMemberName()
        {
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new GatedEnumBlock(), _serviceProvider);
            var model = result.Services.Single(s => s.Identifier == nameof(GatedEnumBlock)).Properties.Single(p => p.Identifier == nameof(GatedEnumBlock.Model));

            Assert.AreEqual("Bricco", model.Runtime!["default"]!.GetValue<string>());
        }

        // ── Pre-Configure application (lifecycle) ─────────────────────────────────────────────────

        [TestMethod]
        public void ApplyParameterValuesBeforeConfigureSoGatesResolve()
        {
            var block = new GatedCountBlock();
            DriveInitialize(block, Param(nameof(GatedCountBlock.PointCount), 3));

            Assert.AreEqual(3, block.PointCount, "The payload value must be applied to the CLR property before Configure.");
        }

        [TestMethod]
        public void FailClosedOnAnUnknownParameterIdentifier()
        {
            Assert.Throws<Exception>(() => DriveInitialize(new GatedCountBlock(), Param("Nonexistent", 1)));
        }

        [TestMethod]
        public void FailClosedOnAParameterDecodeError()
        {
            var badValue = new SetLogicConfigurationPayload.InstantiationParameterValue
                           {
                               Identifier = nameof(GatedCountBlock.PointCount),
                               Value = JsonValue.Create("not-a-number"),
                           };
            Assert.Throws<Exception>(() => DriveInitialize(new GatedCountBlock(), badValue));
        }

        // ── Contract-lookup guard ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void SkipAndWarnWhenAContractMappingTargetsAGatedOutContract()
        {
            var block = new GatedContractBlock(); // PointCount defaults to 1 → Point2Output is gated out and never bound

            // A stale / hand-built payload still maps the excluded contract. The guard must skip it and keep
            // the block up instead of throwing KeyNotFoundException.
            var contractLookup = new Dictionary<string, LogicBlockContractId> { ["Point2Output"] = new("cfg", "Point2Output") };
            var initialize = new InitializeLogicBlock("cfg",
                                                      nameof(GatedContractBlock),
                                                      new Dictionary<string, ServiceIdentifier>(),
                                                      contractLookup,
                                                      new Mock<IServiceProvider>().Object);

            block.HandleMessageAsync(initialize, new Mock<IActorContext>().Object).GetAwaiter().GetResult();

            Assert.IsNull(block.Point2Output, "The gated contract stays excluded; the dangling mapping is skipped, not applied.");
        }

        // ── Persistence (no dormancy) ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ExcludeInstantiationParametersFromPersistenceDiscovery()
        {
            var keys = DiscoveredPersistenceKeys(new GatedCountBlock { PointCount = 1 });

            Assert.IsFalse(keys.Any(k => k.Contains(nameof(GatedCountBlock.PointCount))),
                           "An [InstantiationParameter] must never be auto-persisted (its config channel is the only source of truth).");
        }

        [TestMethod]
        public void NotCaptureAGatedOutComponentsPersistentMembers()
        {
            var keys = DiscoveredPersistenceKeys(new GatedCountBlock { PointCount = 1 }); // Point2/Point3 excluded

            Assert.IsTrue(keys.Any(k => k.Contains("Point1")), "The included Point1 component's persistent state is still captured.");
            Assert.IsFalse(keys.Any(k => k.Contains("Point2") || k.Contains("Point3")),
                           "A gated-out component's [Persistent] members are neither discovered nor captured (no dormancy).");
        }

        [TestMethod]
        public void CaptureAnIncludedInterfaceOnlyComponentsPersistentMembers()
        {
            // The bug the reviewer caught: an included component bound ONLY via its interface (no service
            // members) is absent from the service-binding keys, so a binder-key inference would wrongly skip
            // its [Persistent] state. The gate is re-evaluated instead, so it is captured.
            var keys = DiscoveredPersistenceKeys(new GatedInterfaceOnlyBlock { Count = 2 }); // Probe included

            Assert.IsTrue(keys.Any(k => k.Contains("Probe")),
                          "An included interface-only component's [Persistent] members must be captured (it is bound via the interface, not as a service).");
        }

        [TestMethod]
        public void NotCaptureAnExcludedInterfaceOnlyComponentsPersistentMembers()
        {
            var keys = DiscoveredPersistenceKeys(new GatedInterfaceOnlyBlock { Count = 1 }); // Probe excluded
            Assert.IsFalse(keys.Any(k => k.Contains("Probe")));
        }

        [TestMethod]
        public void DiscardAGatedOutMembersPersistentStateOnExclusion()
        {
            // No dormancy (§10): the SAME member's [Persistent] state is captured when the parameter includes it
            // and discarded when a later config excludes it — a gate flip loses gateway-persisted state.
            Assert.IsTrue(DiscoveredPersistenceKeys(new GatedCountBlock { PointCount = 2 }).Any(k => k.Contains("Point2")),
                          "At PointCount = 2 the Point2 component's persistent state is captured.");
            Assert.IsFalse(DiscoveredPersistenceKeys(new GatedCountBlock { PointCount = 1 }).Any(k => k.Contains("Point2")),
                           "Flipping the gate closed (PointCount = 1) discards Point2's persistent state — no dormancy.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        private HashSet<string> BindLiveServiceIds(LogicBlockBase block)
        {
            var binder = new ServiceBinder();
            var context = InclusionGate.BuildParameterContext(block);
            DeclarativeServiceBinder.BindServicesFromAttributes(block, binder, BindingMode.Live, context);
            return binder.GetAllServicePropertyBindings().Keys.Concat(binder.GetAllServiceMeasuringPointBindings().Keys).ToHashSet(StringComparer.Ordinal);
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Param(string identifier, int value)
        {
            // Integers ride the wire long-backed (PropertyValueCodec encodes int as (long); JSON parsing is
            // JsonElement-backed) — GetValue<long> is what decodes them, so create the node accordingly.
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = JsonValue.Create((long)value) };
        }

        private void DriveInitialize(LogicBlockBase block, params SetLogicConfigurationPayload.InstantiationParameterValue[] parameters)
        {
            var initialize = new InitializeLogicBlock("cfg",
                                                      block.GetType().Name,
                                                      new Dictionary<string, ServiceIdentifier>(),
                                                      new Dictionary<string, LogicBlockContractId>(),
                                                      _serviceProvider,
                                                      parameters.ToList());
            block.HandleMessageAsync(initialize, new Mock<IActorContext>().Object).GetAwaiter().GetResult();
        }

        private List<string> DiscoveredPersistenceKeys(LogicBlockBase block)
        {
            // Drive the real InitializeLogicBlock path so Configure runs the Live-mode binders and populates the
            // block's own ServiceBinder — the resolved set PersistentData consults.
            block.HandleMessageAsync(new InitializeLogicBlock("cfg",
                                                              block.GetType().Name,
                                                              new Dictionary<string, ServiceIdentifier>(),
                                                              new Dictionary<string, LogicBlockContractId>(),
                                                              _serviceProvider),
                                     new Mock<IActorContext>().Object)
                 .GetAwaiter()
                 .GetResult();

            var binder = (ServiceBinder)typeof(LogicBlockBase).GetField("_serviceBinder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(block)!;

            var persistentData = new PersistentData();
            persistentData.Initialize(block, binder, NullLogger.Instance);
            persistentData.CreateSnapshot();
            return persistentData.GetCurrentSnapshot().Select(e => e.Key).ToList();
        }
    }
}