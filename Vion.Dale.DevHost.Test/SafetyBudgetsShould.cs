using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The host's real-time safety budgets, and the two failures that were unreachable while they were
    ///     private constants: a write whose acknowledgement never comes, and a cascade that never settles.
    ///     <para>
    ///         Cross-tier: <c>AC-SCEN-009.10</c> and <c>AC-SCEN-012.6</c> are the scenario page's — what the
    ///         runner records and what the stepper refuses to assume — while <c>AC-CTRL-013.*</c> is this page's
    ///         seam that makes both reachable. Each id is proven here because nothing else can reach either
    ///         failure without waiting out the production budget.
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
                                                 .WithDeterministicStepping()
                                                 .WithSafetyBudgets(new DevHostBudgets { Quiescence = TimeSpan.FromMilliseconds(400) })
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
        [TestProperty("spec", "AC-CTRL-009.7")]
        [TestProperty("spec", "AC-CTRL-013.1")]
        public async Task RefuseWriteOnWindowHostWasBuiltWith()
        {
            // Arrange — the block that never acknowledges, and a window an order of magnitude under the wait
            // below, so the wait is what fails if the host reached for its default instead.
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("rejecting").AddLogicBlock<RejectingWriteBlock>("rejector").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .WithSafetyBudgets(new DevHostBudgets { WriteAcknowledgement = TimeSpan.FromMilliseconds(200) })
                                                 .Build();
            await host.StartAsync();

            // Act
            var refusal =
                await Assert.ThrowsExactlyAsync<ServicePropertyWriteException>(() => host.Control.SetPropertyAsync("rejector", "Rejected", 7).WaitAsync(TimeSpan.FromSeconds(2)));

            // Assert — the refusal names the window it waited out, and the member nobody applied.
            Assert.AreEqual(ServicePropertyWriteException.ReasonUnacknowledged, refusal.Reason);
            Assert.AreEqual("Rejected", refusal.Property);
            StringAssert.Contains(refusal.Message, "0.2s");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-013.1")]
        [DataRow("WriteAcknowledgement", 5.0)]
        [DataRow("StartAcknowledgement", 30.0)]
        [DataRow("StopSequence", 60.0)]
        [DataRow("Quiescence", 10.0)]
        public void CarryDefaultForEveryBudgetCallerNamedNoValueFor(string budget, double seconds)
        {
            // Arrange — the record the builder starts from and every fall-back site constructs when the caller
            // sets none. Reading each budget by name also pins the name a caller sets it under.
            var defaults = new DevHostBudgets();

            // Act
            var value = typeof(DevHostBudgets).GetProperty(budget)?.GetValue(defaults);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(seconds), value, $"{budget} is what the host uses when the caller names no value");
        }
    }
}