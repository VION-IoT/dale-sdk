using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The <c>expect</c> step evaluated against a live host: a point-in-time read of the current value, a
    ///     failure that fails the run and says what it compared, the relational <c>{path}</c> comparand read at
    ///     assert time, and the comparison semantics over numbers, strings and enum-like values.
    ///     <para>
    ///         The comparator block's structural rules — exactly one comparator, the <c>oneOf</c> shape, the
    ///         struct/array refusal, the <c>{path}</c> form — are <c>ScenarioFileShould</c>'s: they need no
    ///         host. This suite is the evaluation half.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ExpectStepShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.7")]
        public async Task HoldForEveryNumericComparator()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-comparators", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "Allocator.Counter", "value": 10 },
                                                  { "expect": { "property": "Allocator.Counter", "above": 9 } },
                                                  { "expect": { "property": "Allocator.Counter", "below": 11 } },
                                                  { "expect": { "property": "Allocator.Counter", "equals": 10 } },
                                                  { "expect": { "property": "Allocator.Counter", "equals": 11, "tolerance": 1 } },
                                                  { "expect": { "property": "Allocator.Counter", "notEquals": 5 } },
                                                  { "expect": { "property": "Allocator.Counter", "oneOf": [9, 10, 11] } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — an int property against double comparands: the comparison is numeric, not typed.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.7")]
        public async Task HoldForStringAndEnumLikeValues()
        {
            // Arrange
            await using var host = BuildModeHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-strings", "topology": "mode-topology",
                                                "steps": [
                                                  { "set": "Mode.State", "value": "Charging" },
                                                  { "expect": { "property": "Mode.State", "equals": "Charging" } },
                                                  { "expect": { "property": "Mode.State", "notEquals": "charging" } },
                                                  { "expect": { "property": "Mode.State", "oneOf": ["Idle", "Charging", "Fault"] } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — the member name is compared case-sensitively, which is what makes it a stable spelling.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.4")]
        public async Task SatisfyOneOfByMembershipWhileWaiting()
        {
            // Arrange — Ramp.L1 climbs one per virtual second, so the set is reached by advancing.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                1,
                                                                1,
                                                                0,
                                                                0,
                                                                0,
                                                                TimeSpan.Zero));
            await using var host = BuildSteppedRampHost(clock);
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "waituntil-oneof", "topology": "struct-stepping",
                                                "steps": [ { "waitUntil": { "property": "Ramp.Ramp.L1", "oneOf": [3, 4] }, "timeoutSeconds": 10 } ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — the same membership semantics a point-in-time expect uses, applied by a wait.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-006.4")]
        public async Task JudgeStructFieldTargetAgainstItsScalarLeaf()
        {
            // Arrange — AllocatedCurrent seeds (L1: 10, L2: 20, L3: 30).
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-struct-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "expect": { "property": "Allocator.AllocatedCurrent.L1", "equals": 10 } },
                                                  { "expect": { "property": "Allocator.AllocatedCurrent.L2", "above": 15 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — over-determined with StructFieldPathShould, which proves the refusal half of the same
            // criterion; this is its positive half through a real read.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.3")]
        public async Task FailRunAndSkipRemainderWhenComparatorDoesNotHold()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-fail", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "Allocator.Counter", "value": 3 },
                                                  { "label": "must be high", "expect": { "property": "Allocator.Counter", "above": 100 } },
                                                  { "set": "Allocator.Counter", "value": 4 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — and the expect itself mutated nothing: the later set never ran, so Counter stays 3.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            CollectionAssert.AreEqual(new[] { ScenarioStepStatus.Ok, ScenarioStepStatus.Failed, ScenarioStepStatus.Skipped }, report.Steps.Select(s => s.Status).ToList());
            Assert.AreEqual(3, host.Control.GetProperty("Allocator", "Counter"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.13")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "above": 100 } }""", "expected Counter above 100, but was 42", DisplayName = "above")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "below": 10 } }""", "expected Counter below 10, but was 42", DisplayName = "below")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "equals": 7 } }""", "expected Counter to equal 7, but was 42", DisplayName = "equals")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "equals": 7, "tolerance": 2 } }""",
                 "expected Counter to equal 7 (±2), but was 42",
                 DisplayName = "equals with tolerance")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "notEquals": 42 } }""", "expected Counter to not equal 42, but was 42", DisplayName = "notEquals")]
        [DataRow("""{ "expect": { "property": "Allocator.Counter", "oneOf": [1, 2, 3] } }""", "expected Counter to be one of [1, 2, 3], but was 42", DisplayName = "oneOf")]
        [DataRow("""{ "expect": { "property": "Allocator.AllocatedCurrent.L1", "equals": 999 } }""",
                 "expected AllocatedCurrent to equal 999, but was 10",
                 DisplayName = "struct field leaf")]
        public async Task NameTheTargetTheBoundAndTheActualValueOnFailure(string step, string expectedDetail)
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse($$"""
                                                {
                                                  "version": 1, "id": "expect-detail", "topology": "struct-topology",
                                                  "steps": [
                                                    { "set": "Allocator.Counter", "value": 42 },
                                                    {{step}}
                                                  ]
                                                }
                                                """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — the detail is the whole diagnosis in a CI log, so it is asserted verbatim.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(expectedDetail, report.Steps[1].Detail);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.14")]
        public async Task ReadRelationalComparandAtAssertTime()
        {
            // Arrange — PointA above PointB, then the pair inverted so the same assertion fails.
            await using var host = BuildHost();
            await host.StartAsync();

            var holds = ScenarioFile.Parse("""
                                           {
                                             "version": 1, "id": "expect-relational-pass", "topology": "struct-topology",
                                             "steps": [
                                               { "set": "DualPoint.PointA.Limit", "value": 8 },
                                               { "set": "DualPoint.PointB.Limit", "value": 5 },
                                               { "expect": { "property": "DualPoint.PointA.Limit", "above": { "path": "DualPoint.PointB.Limit" } } },
                                               { "expect": { "property": "DualPoint.PointA.Limit", "notEquals": { "path": "DualPoint.PointB.Limit" } } }
                                             ]
                                           }
                                           """);
            var inverted = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-relational-fail", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "DualPoint.PointA.Limit", "value": 5 },
                                                  { "set": "DualPoint.PointB.Limit", "value": 9 },
                                                  { "expect": { "property": "DualPoint.PointA.Limit", "above": { "path": "DualPoint.PointB.Limit" } } }
                                                ]
                                              }
                                              """);

            // Act
            var passing = await ScenarioRunner.RunAsync(holds, host.Control);
            var failing = await ScenarioRunner.RunAsync(inverted, host.Control);

            // Assert — the comparand's value at assert time is what the detail carries, and the member it came
            // from is named, so the reader can tell a relational failure from a literal one.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, passing.Status, Join(passing));
            Assert.AreEqual(ScenarioRunStatus.Failed, failing.Status, Join(failing));
            StringAssert.Contains(failing.Steps[2].Detail, "above 9 (from Limit)");
            StringAssert.Contains(failing.Steps[2].Detail, "but was 5");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.7")]
        public async Task RenderEveryComparatorInReportArgument()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-report", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "Allocator.Counter", "value": 10 },
                                                  { "expect": { "property": "Allocator.Counter", "above": 9 } },
                                                  { "expect": { "property": "Allocator.Counter", "oneOf": [9, 10] } },
                                                  { "expect": { "property": "DualPoint.PointA.Limit", "equals": { "path": "DualPoint.PointB.Limit" } } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert — a relational comparand renders as its path so the argument says what will be compared.
            Assert.AreEqual("Allocator.Counter", report.Steps[1].Target);
            Assert.AreEqual("> 9", report.Steps[1].Argument);
            Assert.AreEqual("one of [9, 10]", report.Steps[2].Argument);
            Assert.AreEqual("== {DualPoint.PointB.Limit}", report.Steps[3].Argument);
        }

        private static IDevHost BuildHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("struct-topology")
                                                .AddLogicBlock<AllocatorBlock>("Allocator")
                                                .AddLogicBlock<CollisionBlock>("Collision")
                                                .AddLogicBlock<DualPointBlock>("DualPoint")
                                                .Build();

            return DevHostBuilder.Create().WithDi<StructFieldDependencyInjection>().WithConfiguration(config).Build();
        }

        private static IDevHost BuildSteppedRampHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("struct-stepping").AddLogicBlock<RampBlock>("Ramp").Build();

            return DevHostBuilder.Create().WithDi<StructFieldDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }

        private static IDevHost BuildModeHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("mode-topology").AddLogicBlock<ModeBlock>("Mode").Build();

            return DevHostBuilder.Create().WithDi<ModeDependencyInjection>().WithConfiguration(config).Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }

    /// <summary>A block with a writable string property — the target for <c>oneOf</c> on a string/enum value.</summary>
    [LogicBlock(Name = "Mode")]
    public class ModeBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "State")]
        public string State { get; set; } = "Idle";

        public ModeBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>DI registration for the mode fixture.</summary>
    public class ModeDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<ModeBlock>();
        }
    }
}