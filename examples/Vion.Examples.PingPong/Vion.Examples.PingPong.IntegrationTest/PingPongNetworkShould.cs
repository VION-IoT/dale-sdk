using System;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Control;
using Vion.Examples.PingPong.LogicBlocks;
using Xunit;

namespace Vion.Examples.PingPong.IntegrationTest
{
    /// <summary>
    ///     Headless integration test (RFC 0003): boots the real wired Ping ↔ Pong network with no web UI and
    ///     drives / observes it entirely through <see cref="IDevHostControl" /> — the agent / CI scenario.
    ///     <para>
    ///         This is the tier the single-block TestKit unit tests (PingShould / PongShould) can't cover: a
    ///         wiring bug in the inter-block contract (e.g. the request never actually leaves Ping) is invisible
    ///         to a single-SUT test because it injects the very response the missing request was meant to fetch.
    ///         Here the whole network runs with real message passing, so the loop only turns if the wiring holds.
    ///     </para>
    /// </summary>
    public class PingPongNetworkShould
    {
        private static IDevHost BuildHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<Ping>()
                                                .AddLogicBlock<Pong>()
                                                .AutoConnect()
                                                .Build();

            return DevHostBuilder.Create()
                                 .WithDi<DependencyInjection>()
                                 .WithConfiguration(config)
                                 .Build();
        }

        [Fact]
        public async Task ExchangeMessages_AndExposeThemThroughTheControlSurface()
        {
            await using var host = BuildHost();
            await host.StartAsync();
            var control = host.Control;

            // Topology: both blocks are wired and discoverable.
            Assert.Equal(2, control.ListLogicBlocks().Count);

            // The loop is live: Pong reports throughput once its per-second timer ticks. If Ping → Pong
            // messaging were broken, this would never arrive — exactly the wiring bug a single-block test misses.
            var pongs = await control.WaitForAsync(
                e => e is ServiceMeasuringPointChanged { MeasuringPoint: "PongsPerSecond" } mp && Convert.ToInt32(mp.Value) > 0
                         ? (object)mp.Value!
                         : null,
                timeout: TimeSpan.FromSeconds(15));
            Assert.NotNull(pongs);

            // The message tap captured the inter-actor traffic Pong received — the multi-block analogue of
            // TestKit's Verify*, and the highest-yield diagnostic for a "request never sent" bug.
            Assert.NotEmpty(control.RecordedMessages("Pong"));

            // Drive a writable knob and observe the effect: pausing Ping is a [ServiceProperty].
            await control.SetPropertyAsync("Ping", "Pause", true);
            var paused = await control.WaitForAsync(
                e => e is ServicePropertyChanged { Property: "Pause" } sp && Equals(sp.Value, true) ? (object)true : null,
                timeout: TimeSpan.FromSeconds(15));
            Assert.NotNull(paused);
            Assert.Equal(true, control.GetProperty("Ping", "Pause"));
        }
    }
}
