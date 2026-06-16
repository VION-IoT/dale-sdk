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
    ///     The <c>expect</c> auto-assert scenario step (RFC 0006 "Assert tier"): a deterministic,
    ///     point-in-time assertion on the CURRENT value of a name path. A failing <c>expect</c> FAILS the run
    ///     (the CI-failing tier). Covers every comparator (above / below / equals+tolerance / notEquals /
    ///     oneOf), the relational <c>{path}</c> comparand (resolved at assert time), struct field-path
    ///     targets, <c>oneOf</c> on <c>waitUntil</c> too, and the structural validation.
    /// </summary>
    [TestClass]
    public class ExpectStepShould
    {
        // ── passing + failing (a failing expect fails the run, with the value in the detail) ──────────────

        [TestMethod]
        public async Task PassWhenTheCurrentValueSatisfiesTheComparator()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            // AllocatedCurrent seeds (10, 20, 30, 0); Counter is 0 by default. settle is not needed — the
            // value is already settled after Ready.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-pass", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "Allocator.Counter", "value": 7 },
                                                  { "expect": { "property": "Allocator.Counter", "equals": 7 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        public async Task FailTheRunWhenTheComparatorDoesNotHold_WithTheValueInTheDetail()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // A failing expect fails the whole run, fails its step, and skips the rest.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Failed, report.Steps[1].Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail, "above 100");
            StringAssert.Contains(report.Steps[1].Detail, "but was 3");
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[2].Status, Join(report));

            // The expect must NOT have mutated state — Counter stays 3 (the later set was skipped).
            Assert.AreEqual(3, host.Control.GetProperty("Allocator", "Counter"));
        }

        // ── each comparator ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task SupportEveryComparator_AboveBelowEqualsToleranceNotEqualsOneOf()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        public async Task FailOneOf_WithTheNotOneOfDetail()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-oneof-fail", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "Allocator.Counter", "value": 42 },
                                                  { "expect": { "property": "Allocator.Counter", "oneOf": [1, 2, 3] } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail, "one of");
            StringAssert.Contains(report.Steps[1].Detail, "but was 42");
        }

        [TestMethod]
        public async Task SupportOneOf_OnAStringEnumLikeValue()
        {
            await using var host = BuildModeHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-oneof-string", "topology": "mode-topology",
                                                "steps": [
                                                  { "set": "Mode.State", "value": "Charging" },
                                                  { "expect": { "property": "Mode.State", "oneOf": ["Idle", "Charging", "Fault"] } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        // ── relational comparand: expect A above {path:B} ─────────────────────────────────────────────────

        [TestMethod]
        public async Task PassRelationalComparand_ResolvedAtAssertTime()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            // PointA.Limit = 8 > PointB.Limit = 5 → expect PointA.Limit above {path: PointB.Limit} holds.
            var scenario = ScenarioFile.Parse("""
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        public async Task FailRelationalComparand_WhenItDoesNotHold_ComparandReadAtAssertTime()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            // PointA.Limit = 5 is NOT above PointB.Limit = 9. The comparand value (9) is read at assert time,
            // so the failure detail must carry it.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-relational-fail", "topology": "struct-topology",
                                                "steps": [
                                                  { "set": "DualPoint.PointA.Limit", "value": 5 },
                                                  { "set": "DualPoint.PointB.Limit", "value": 9 },
                                                  { "expect": { "property": "DualPoint.PointA.Limit", "above": { "path": "DualPoint.PointB.Limit" } } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[2].Detail, "above 9");
            StringAssert.Contains(report.Steps[2].Detail, "but was 5");
        }

        // ── struct field target ────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task ExpectOnAStructField()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            // AllocatedCurrent seeds (L1: 10, L2: 20, L3: 30) — assert the scalar field leaf directly.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-struct-field", "topology": "struct-topology",
                                                "steps": [
                                                  { "expect": { "property": "Allocator.AllocatedCurrent.L1", "equals": 10 } },
                                                  { "expect": { "property": "Allocator.AllocatedCurrent.L2", "above": 15 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        public async Task FailOnAStructField_WithTheFieldValueInTheDetail()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "expect-struct-field-fail", "topology": "struct-topology",
                                                "steps": [
                                                  { "expect": { "property": "Allocator.AllocatedCurrent.L1", "equals": 999 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "but was 10");
        }

        // ── oneOf on waitUntil too ───────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task SupportOneOf_OnWaitUntil()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            await using var host = BuildSteppedRampHost(clock);
            await host.StartAsync();

            // Ramp.L1 climbs 0, 1, 2, 3, … each virtual second. waitUntil L1 ∈ {3, 4} is satisfied by
            // advancing virtual time hop-by-hop.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "waituntil-oneof", "topology": "struct-stepping",
                                                "steps": [
                                                  { "waitUntil": { "property": "Ramp.Ramp.L1", "oneOf": [3, 4] }, "timeoutSeconds": 10 }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        // ── report rendering ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task RenderTheExpectComparatorInTheReportArgument()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual("Allocator.Counter", report.Steps[1].Target);
            Assert.AreEqual("> 9", report.Steps[1].Argument);
            Assert.AreEqual("one of [9, 10]", report.Steps[2].Argument);
            Assert.AreEqual("== {DualPoint.PointB.Limit}", report.Steps[3].Argument);
        }

        // ── structural validation ──────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void RejectExpectInSetup()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "setup": [ { "expect": { "property": "A.B", "equals": 1 } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("setup entries")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectTwoComparators()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "expect": { "property": "A.B", "above": 1, "below": 2 } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("exactly one of above")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectEmptyOrObjectContainingOneOf()
        {
            var empty = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                               {
                                                                                                 "version": 1, "id": "x", "topology": "t",
                                                                                                 "steps": [ { "expect": { "property": "A.B", "oneOf": [] } } ]
                                                                                               }
                                                                                               """));
            Assert.IsTrue(empty.Errors.Any(m => m.Contains("oneOf must be a non-empty array")), string.Join("; ", empty.Errors));

            var withObject = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                    {
                                                                                                      "version": 1, "id": "x", "topology": "t",
                                                                                                      "steps": [ { "expect": { "property": "A.B", "oneOf": [1, { "x": 1 }] } } ]
                                                                                                    }
                                                                                                    """));
            Assert.IsTrue(withObject.Errors.Any(m => m.Contains("oneOf elements must be scalars")), string.Join("; ", withObject.Errors));
        }

        [TestMethod]
        public void RejectANonPathObjectComparand()
        {
            // The ONLY allowed object comparand is { "path": "…" }. A struct literal stays rejected.
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "expect": { "property": "A.B", "equals": { "l1": 1, "l2": 2 } } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("does not compare structs/arrays")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void AcceptThePathObjectComparand_Structurally()
        {
            var file = ScenarioFile.Parse("""
                                          {
                                            "version": 1, "id": "x", "topology": "t",
                                            "steps": [ { "expect": { "property": "A.B", "above": { "path": "A.C" } } } ]
                                          }
                                          """);
            Assert.AreEqual("expect", file.Steps![0].Kind);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────

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
