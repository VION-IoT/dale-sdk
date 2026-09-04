using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The host's real-time safety budgets, and the two failures that were unreachable while they were
    ///     private constants: a write whose acknowledgement never comes, and a cascade that never settles.
    ///     <para>
    ///         Cross-tier: <c>AC-SCEN-009.10</c> and <c>AC-SCEN-012.6</c> are the scenario page's — what the
    ///         runner records and what the stepper refuses to assume. This suite owns the seam that makes both
    ///         reachable, and pins the scenario-side claims from it because nothing else can.
    ///     </para>
    /// </summary>
    [TestClass]
    public class SafetyBudgetsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.10")]
        public async Task RecordRejectedWriteWhoseAcknowledgementConsumedItsWindow()
        {
            // Arrange — a block that throws from its setter, and a window short enough to reach.
            var window = TimeSpan.FromMilliseconds(400);
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("rejecting").AddLogicBlock<RejectingWriteBlock>("rejector").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .WithSafetyBudgets(new DevHostBudgets { WriteAcknowledgement = window })
                                                 .Build();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1,
                                                "id": "rejected-write",
                                                "topology": "rejecting",
                                                "steps": [ { "set": "rejector.Rejected", "value": 7 } ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control, new ScenarioRunOptions { WriteAcknowledgementWindow = window });

            // Assert — the step fails because a block exception was logged for this write, not merely because
            // the window elapsed. The middleware's line names the message it was handling, which is what the
            // detection matches on; the block's own error text is on the health surface, not in this detail.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, report.Steps[0].Detail);
            StringAssert.Contains(report.Steps[0].Detail!, "write appears rejected");
            StringAssert.Contains(report.Steps[0].Detail!, "SetServicePropertyValueRequest");
            StringAssert.Contains(host.Control.RecordedFailures("rejector")[0].Error, RejectingWriteBlock.RefusalMessage);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.6")]
        public async Task KeepWaitingForQuiescenceThenFailNamingPredicate()
        {
            // Arrange — a handler that occupies the actor for longer than the budget, so the exact predicate
            // cannot hold while it runs. Without the seam this failure costs ten real seconds to reach.
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("slow").AddLogicBlock<SlowHandlerBlock>("slow").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .WithDeterministicStepping(null, TimeSpan.FromMilliseconds(400))
                                                 .Build();
            await host.StartAsync();

            // Act
            var refusal = await Assert.ThrowsExactlyAsync<TimeoutException>(() => host.Control.AdvanceAsync(TimeSpan.FromSeconds(2)));

            // Assert — it says which predicate never held, rather than only how long it waited.
            StringAssert.Contains(refusal.Message, "MailboxDepth");
            StringAssert.Contains(refusal.Message, "never held");
            StringAssert.Contains(refusal.Message, "0.4s");
        }

        [TestMethod]
        public async Task GiveWriteWindowHostWasBuiltWith()
        {
            // Arrange — the same rejecting block, but a window a healthy write cannot exhaust.
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("rejecting").AddLogicBlock<RejectingWriteBlock>("rejector").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .WithSafetyBudgets(new DevHostBudgets { WriteAcknowledgement = TimeSpan.FromMilliseconds(600) })
                                                 .Build();
            await host.StartAsync();

            // Act — a write the block applies acknowledges promptly and never approaches the window.
            var elapsed = Stopwatch.StartNew();
            await host.Control.SetPropertyAsync("rejector", "Rejected", 0);
            elapsed.Stop();

            // Assert
            Assert.IsLessThan(TimeSpan.FromMilliseconds(600), elapsed.Elapsed, "an applied write acknowledges on its own round trip, nowhere near the window");
        }
    }
}
