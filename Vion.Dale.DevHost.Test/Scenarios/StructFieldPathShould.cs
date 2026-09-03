using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Struct field paths in scenario name resolution (watch / waitUntil): a scalar FIELD of a
    ///     struct-typed observable — e.g. <c>Allocator.AllocatedCurrent.L1</c> — is addressable, while a
    ///     whole struct still is not. Covers the field-path parse (incl. the 3-segment service-vs-field
    ///     ambiguity resolved against the config), schema validation (helpful PascalCase suggestions),
    ///     runtime leaf extraction, and a deterministic stepped wait on a struct field.
    /// </summary>
    [TestClass]
    public class StructFieldPathShould
    {
        // ── waitUntil on a struct field (above / below / equals) ──────────────────────────────────────

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.4")]
        [TestProperty("spec", "AC-SCEN-006.1")]
        public async Task ResolveAndEvaluateWaitUntilOnStructFieldWithEveryComparator()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // The block seeds AllocatedCurrent = (L1: 10, L2: 20, L3: 30) on Ready.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "struct-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "waitUntil": { "property": "Allocator.AllocatedCurrent.L1", "above": 5 }, "timeoutSeconds": 5 },
                                                  { "waitUntil": { "property": "Allocator.AllocatedCurrent.L2", "below": 25 }, "timeoutSeconds": 5 },
                                                  { "waitUntil": { "property": "Allocator.AllocatedCurrent.L3", "equals": 30 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.7")]
        public async Task ResolveAndEvaluateWaitUntilOnStructMeasuringPointField()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // MeasuredCurrent is a struct [ServiceMeasuringPoint]; its L1 field is seeded to 100.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "struct-mp-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "waitUntil": { "property": "Allocator.MeasuredCurrent.L1", "equals": 100 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.1")]
        public async Task ResolveWatchOnStructField()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // A watch-only scenario smokes the field path through up-front validation.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "struct-watch", "topology": "struct-topology",
                                                "watch": [ "Allocator.AllocatedCurrent.L1", "Allocator.MeasuredCurrent.L2" ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsEmpty(report.ValidationErrors);
        }

        // ── the 3-segment ambiguity ───────────────────────────────────────────────────────────────────

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.3")]
        public async Task ResolveServiceQualifiedPathWhenSegmentOneNamesService()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // PointA is a SERVICE of DualPoint, so DualPoint.PointA.Limit is a service-qualified member —
            // NOT a field path. (Limit is a scalar, no field path.)
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "svc-qualified", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "DualPoint.PointA.Limit", "value": 4.5 },
                                                  { "waitUntil": { "property": "DualPoint.PointA.Limit", "equals": 4.5 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual(4.5, host.Control.GetProperty("DualPoint", "PointA", "Limit"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.3")]
        public async Task ResolvePropertyPlusFieldPathWhenSegmentOneNamesMember()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // AllocatedCurrent is NOT a service of Allocator (it is a struct member), so the 3-segment
            // Allocator.AllocatedCurrent.L1 is a member + field-path reading.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "member-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "waitUntil": { "property": "Allocator.AllocatedCurrent.L1", "equals": 10 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.4")]
        public async Task ErrorWhenSegmentOneNamesBothServiceAndMember()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // On Collision: "Allocated" is the identifier of one nested service AND a struct member of
            // another nested service. Collision.Allocated.L1 is genuinely ambiguous.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "ambiguous", "topology": "struct-topology",
                                                "watch": [ "Collision.Allocated.L1" ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("ambiguous") && e.Contains("Allocated")), Join(report));
        }

        // ── whole-struct still guarded ────────────────────────────────────────────────────────────────

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.3")]
        public async Task RejectWholeStructTargetWhenNoFieldPath()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // No field path → the struct itself is the target → still not comparable in v1.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "whole-struct", "topology": "struct-topology",
                                                "steps": [
                                                  { "waitUntil": { "property": "Allocator.AllocatedCurrent", "equals": 1 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("object-typed member") && e.Contains("not comparable")), Join(report));
        }

        // ── unknown field → PascalCase suggestion ─────────────────────────────────────────────────────

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.2")]
        public async Task SuggestPascalCaseFieldNameOnFieldTypo()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // "Neutralcurrent" mis-cases the interior capital; it does not match the camelCase key
            // "neutralCurrent". The suggestion reports the canonical PascalCase "NeutralCurrent".
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "field-typo", "topology": "struct-topology",
                                                "watch": [ "Allocator.AllocatedCurrent.Neutralcurrent" ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("has no field 'Neutralcurrent'") && e.Contains("did you mean 'NeutralCurrent'")), Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.4")]
        [TestProperty("spec", "AC-SCEN-006.2")]
        public async Task RejectAboveBelowOnNonNumericFieldAndOnFieldOfScalar()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // A field path off a scalar member is rejected — Counter is an int, not a struct.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "scalar-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "waitUntil": { "property": "Allocator.Counter.Nope", "equals": 1 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("is not a struct") || e.Contains("has no field")), Join(report));
        }

        // ── deterministic stepped waitUntil on a struct field ─────────────────────────────────────────

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.1")]
        public async Task RunSteppedWaitUntilOnStructFieldDeterministically()
        {
            // Arrange
            for (var run = 0; run < 5; run++)
            {
                var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                    1,
                                                                    1,
                                                                    0,
                                                                    0,
                                                                    0,
                                                                    TimeSpan.Zero));
                await using var host = BuildSteppedHost(clock);
                await host.StartAsync();

                // RampBlock's [Timer(1)] raises Ramp.L1 by 1 each virtual second from 0. waitUntil L1 > 2
                // must be satisfied by advancing virtual time hop-by-hop (deterministic, near-instant).
                var scenario = ScenarioFile.Parse("""
                                                  {
                                                    "version": 1, "id": "stepped-struct", "topology": "struct-stepping",
                                                    "steps": [
                                                      { "waitUntil": { "property": "Ramp.Ramp.L1", "above": 2 }, "timeoutSeconds": 10 }
                                                    ]
                                                  }
                                                  """);

                // Act
                var report = await ScenarioRunner.RunAsync(scenario, host.Control);

                // Assert
                Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, $"run {run}: {Join(report)}");
                StringAssert.Contains(report.Steps[0].Detail, "virtual s", $"run {run}: {report.Steps[0].Detail}");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.1")]
        [DataRow("Allocator", "is not a name path", DisplayName = "one segment")]
        [DataRow("Allocator..Counter", "is not a name path", DisplayName = "empty inner segment")]
        [DataRow("Allocator.", "is not a name path", DisplayName = "trailing dot")]
        [DataRow(".Counter", "is not a name path", DisplayName = "leading dot")]
        [DataRow("Allocator.  ", "is not a name path", DisplayName = "whitespace segment")]
        public async Task RefuseNamePathWithoutTwoNonEmptySegments(string path, string expectedError)
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act
            var errors = new List<string>();
            new ScenarioResolver(host.Control.GetConfiguration()).ResolveProperty(path, "watch[0]", errors);

            // Assert
            Assert.IsNotEmpty(errors);
            StringAssert.Contains(errors[0], expectedError);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.5")]
        public async Task AcceptTwoSegmentFormWhenExactlyOneServiceCarriesMember()
        {
            // Arrange — Allocator has one service, so Counter is unambiguous; Collision has two, and only
            // one of them carries Limit.
            await using var host = BuildHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            // Act
            var errors = new List<string>();
            var counter = resolver.ResolveProperty("Allocator.Counter", "steps[0]", errors);
            var limit = resolver.ResolveProperty("Collision.Limit", "steps[1]", errors);

            // Assert — the qualified form resolves to the same member, which is what makes the short one safe.
            Assert.IsEmpty(errors, string.Join("; ", errors));
            Assert.AreEqual("Counter", counter!.PropertyName);
            Assert.AreEqual("Limit", limit!.PropertyName);
            Assert.AreEqual("Allocated", limit.ServiceIdentifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.5")]
        public async Task ReadNullableWidenedLeafAsItsNonNullType()
        {
            // Arrange — Nullable.Reading is a nullable struct, so its schema type is ["object","null"] and
            // its numeric leaf is ["number","null"]. Without the normalisation, above/below on the leaf
            // would be refused as non-numeric and the whole-struct refusal would not fire.
            await using var host = BuildHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            // Act
            var leafErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { WaitUntil = new ScenarioWaitUntil { Property = "Nullable.Reading.L1", Above = Json("1") } }, "steps[0]", leafErrors);

            var wholeErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { WaitUntil = new ScenarioWaitUntil { Property = "Nullable.Reading", EqualTo = Json("1") } }, "steps[1]", wholeErrors);

            // Assert
            Assert.IsEmpty(leafErrors, string.Join("; ", leafErrors));
            Assert.IsNotEmpty(wholeErrors);
            StringAssert.Contains(wholeErrors[0], "object-typed member");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.6")]
        public async Task YieldNullLeafWhenStructItselfNull()
        {
            // Arrange — the block leaves Reading null, so the field path reads through nothing.
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "null-leaf", "topology": "struct-topology",
                                                "watch": ["Nullable.Reading.L1"],
                                                "steps": [ { "expect": { "property": "Nullable.Reading.L1", "equals": null } } ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — a null intermediate is a state, not a run failure, and the watch sampler survives it
            // even though it runs outside the per-step try/catch.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, string.Join("; ", report.ValidationErrors));
            Assert.HasCount(2, report.WatchTrace);
            Assert.IsNull(report.WatchTrace[0].Values["Nullable.Reading.L1"]);
        }

        private static JsonElement Json(string raw)
        {
            return JsonDocument.Parse(raw).RootElement.Clone();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

        private static IDevHost BuildHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("struct-topology")
                                                .AddLogicBlock<AllocatorBlock>("Allocator")
                                                .AddLogicBlock<CollisionBlock>("Collision")
                                                .AddLogicBlock<DualPointBlock>("DualPoint")
                                                .AddLogicBlock<NullableStructBlock>("Nullable")
                                                .Build();
            return DevHostBuilder.Create().WithDi<StructFieldDependencyInjection>().WithConfiguration(config).Build();
        }

        private static IDevHost BuildSteppedHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("struct-stepping").AddLogicBlock<RampBlock>("Ramp").Build();
            return DevHostBuilder.Create().WithDi<StructFieldDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }

    /// <summary>
    ///     A block whose struct <c>[ServiceProperty]</c> is NULLABLE and left null — the shape that widens the
    ///     schema type to <c>["object","null"]</c> and makes a field path read through nothing.
    /// </summary>
    [LogicBlock(Name = "Nullable")]
    public class NullableStructBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Reading")]
        public PhaseCurrents? Reading { get; set; }

        public NullableStructBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A flat phase-current struct — the struct-typed observable under test. <c>NeutralCurrent</c> has
    ///     interior capitals on purpose, so a casing typo (<c>Neutralcurrent</c>) exercises the canonical
    ///     PascalCase "did you mean" suggestion.
    /// </summary>
    public readonly record struct PhaseCurrents(
        [StructField(Title = "L1", Unit = "A")]
        double L1,
        [StructField(Title = "L2", Unit = "A")]
        double L2,
        [StructField(Title = "L3", Unit = "A")]
        double L3,
        [StructField(Title = "Neutral", Unit = "A")]
        double NeutralCurrent);

    /// <summary>
    ///     A block exposing a struct <c>[ServiceProperty]</c> and a struct <c>[ServiceMeasuringPoint]</c>,
    ///     plus a scalar <c>Counter</c> — the canonical target for struct field-path resolution.
    /// </summary>
    [LogicBlock(Name = "Allocator")]
    public class AllocatorBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Allocated current")]
        public PhaseCurrents AllocatedCurrent { get; set; }

        [ServiceMeasuringPoint(Title = "Measured current")]
        public PhaseCurrents MeasuredCurrent { get; private set; }

        [ServiceProperty(Title = "Counter")]
        public int Counter { get; set; }

        public AllocatorBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            AllocatedCurrent = new PhaseCurrents(10, 20, 30, 0);
            MeasuredCurrent = new PhaseCurrents(100, 200, 300, 0);
        }
    }

    /// <summary>
    ///     Two nested interface-bound services where one service's IDENTIFIER (<c>Allocated</c>) equals a
    ///     struct MEMBER name on the other service — the genuine 3-segment ambiguity
    ///     (<c>Collision.Allocated.L1</c> could be service+member or member+field).
    /// </summary>
    [LogicBlock(Name = "Collision")]
    public class CollisionBlock : LogicBlockBase
    {
        [LogicBlockInterfaceBinding(typeof(ISink))]
        public AllocatedSink Allocated { get; }

        [LogicBlockInterfaceBinding(typeof(ISink))]
        public ReadingSink Reading { get; }

        public CollisionBlock(ILogger logger) : base(logger)
        {
            Allocated = new AllocatedSink();
            Reading = new ReadingSink();
        }

        protected override void Ready()
        {
        }

        public class AllocatedSink : ISink
        {
            [ServiceProperty(Title = "Limit")]
            public double Limit { get; set; } = 1.0;

            public PollLink.Ack HandleRequest(PollLink.Poll request)
            {
                return new PollLink.Ack();
            }
        }

        public class ReadingSink : ISink
        {
            // A struct member whose name collides with the OTHER service's identifier.
            [ServiceProperty(Title = "Allocated")]
            public PhaseCurrents Allocated { get; set; }

            public PollLink.Ack HandleRequest(PollLink.Poll request)
            {
                return new PollLink.Ack();
            }
        }
    }

    /// <summary>Ramps the struct field <c>Ramp.L1</c> by 1 each virtual second — the stepped target.</summary>
    [LogicBlock(Name = "Ramp")]
    public class RampBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ramp")]
        public PhaseCurrents Ramp { get; private set; }

        public RampBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            Ramp = new PhaseCurrents(Ramp.L1 + 1, Ramp.L2, Ramp.L3, 0);
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>DI registration for the struct field-path fixtures.</summary>
    public class StructFieldDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<AllocatorBlock>();
            serviceCollection.AddTransient<CollisionBlock>();
            serviceCollection.AddTransient<DualPointBlock>();
            serviceCollection.AddTransient<RampBlock>();
            serviceCollection.AddTransient<NullableStructBlock>();
        }
    }
}