using System;
using System.Linq;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The host's start-and-health surface: what a start guarantees, what it refuses, and what a caller can
    ///     read afterwards about a block that did not come up. The actor middleware catches a handler throw and
    ///     carries on, so a block that fails to configure, to bind or to start leaves no trace on its own state
    ///     — the host used to report itself started over it with nothing but one log line to say otherwise.
    /// </summary>
    [TestClass]
    public class HostHealthShould
    {
        [TestMethod]
        public async Task ReportTheFailureOfABlockThatCouldNotConfigure()
        {
            // Arrange — a block whose Ready() throws during the configuration phase. The send that carries it
            // is fire-and-forget, so the throw never reaches the start, and the block still acknowledges start.
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<FailingConfigureBlock>("bad").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();

            // Act — the start succeeds, which is exactly the problem this surface exists for.
            await host.StartAsync();
            var failures = host.Control.RecordedFailures();

            // Assert
            Assert.IsNotEmpty(failures, "a block whose configuration threw must be reported, not only logged");
            Assert.AreEqual("bad", failures[0].LogicBlock, "the failure names the block by the name it was wired under");
            StringAssert.Contains(failures[0].Error, FailingConfigureBlock.FailureMessage);
        }

        [TestMethod]
        public async Task ReportNoFailureForAHostWhoseBlocksAllCameUp()
        {
            // Arrange
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();

            // Act
            await host.StartAsync();

            // Assert — the signal has to be quiet on a healthy host or it says nothing on a broken one.
            Assert.IsEmpty(host.Control.RecordedFailures());
        }

        [TestMethod]
        public async Task ReportOnlyTheNamedBlocksFailures()
        {
            // Arrange — one broken block beside a healthy one.
            var configuration = DevConfigurationBuilder.Create()
                                                       .AddLogicBlock<FailingConfigureBlock>("bad")
                                                       .AddLogicBlock<CounterBlock>("counter")
                                                       .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();
            await host.StartAsync();

            // Act
            var forBadBlock = host.Control.RecordedFailures("bad");
            var forHealthyBlock = host.Control.RecordedFailures("counter");
            var forUnknownBlock = host.Control.RecordedFailures("nosuchblock");

            // Assert
            Assert.IsNotEmpty(forBadBlock);
            Assert.IsEmpty(forHealthyBlock);
            Assert.IsEmpty(forUnknownBlock);
        }

        [TestMethod]
        public async Task FailAStartNoBlockAcknowledgesRatherThanWaitOnAClockNothingAdvances()
        {
            // Arrange — a stepped host and a block whose Starting() throws, so its acknowledgement never comes.
            // The acknowledgement wait is routed through the injected clock, which nothing advances during a
            // boot: without the real-time backstop this start never returns at all.
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<FailingStartBlock>("bad").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .WithDeterministicStepping()
                                                 .WithSafetyBudgets(new DevHostBudgets { StartAcknowledgement = TimeSpan.FromMilliseconds(500) })
                                                 .Build();

            // Act
            var refusal = await Assert.ThrowsExactlyAsync<TimeoutException>(() => host.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)));

            // Assert
            StringAssert.Contains(refusal.Message, "acknowledged start");
            Assert.IsNotEmpty(host.Control.RecordedFailures("bad"), "the failure that stopped the acknowledgement must name the block");
        }

        [TestMethod]
        public async Task RefuseASecondStartOfOneHost()
        {
            // Arrange
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();
            await host.StartAsync();

            // Act — a second start would add a second copy of every hosted service and rebind the port.
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            // Assert
            StringAssert.Contains(refusal.Message, "already started");
            Assert.AreEqual(0, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")), "the first host must still be serving");
        }

        [TestMethod]
        public void RefuseANonPositiveSafetyBudget()
        {
            // Arrange
            var builder = DevHostBuilder.Create();

            // Act + Assert — a zero or negative backstop is no backstop; refuse it here rather than at the
            // wait it would fail to bound.
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithSafetyBudgets(new DevHostBudgets { Quiescence = TimeSpan.Zero }));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithSafetyBudgets(new DevHostBudgets { WriteAcknowledgement = TimeSpan.FromSeconds(-1) }));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithDeterministicStepping(null, TimeSpan.Zero));
        }
    }
}
