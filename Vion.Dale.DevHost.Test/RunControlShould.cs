using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>Ticks a counter once per second via [Timer] — the pause gate's guinea pig.</summary>
    [LogicBlock(Name = "Ticker")]
    public class TickerBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ticks")]
        public int Ticks { get; private set; }

        public TickerBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            Ticks++;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     Run-control tests (R2): the pause gate (hold timer fires, replay on resume — semantics
    ///     documented on <see cref="Control.IDevHostControl.Pause" />) and the reset supervisor
    ///     (<see cref="DevHostWebRunner.RunAsync(Func{IDevHost}, int, CancellationToken)" /> recycles the
    ///     host on request, same port).
    /// </summary>
    [TestClass]
    public class RunControlShould
    {
        [TestMethod]
        public async Task Pause_HoldsTimerTicks_AndResumeReplaysThem()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            // Let it tick at least once so the timer chain is alive.
            var ticking = await PollAsync(() => TickCount(host) >= 1, TimeSpan.FromSeconds(10));
            Assert.IsTrue(ticking, "The ticker should tick before the pause.");

            host.Control.Pause();
            Assert.IsTrue(host.Control.IsPaused);

            // The documented allowance: in-flight fires (scheduled before the pause) may still land — and
            // under parallel test-assembly load they can land LATE. Wait for quiescence (count stable for
            // 2 s) instead of sampling at a fixed offset, then assert sustained silence.
            var last = TickCount(host);
            var stableSince = DateTime.UtcNow;
            var quiesceDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < quiesceDeadline && DateTime.UtcNow - stableSince < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(100);
                var now = TickCount(host);
                if (now != last)
                {
                    last = now;
                    stableSince = DateTime.UtcNow;
                }
            }

            Assert.IsTrue(DateTime.UtcNow - stableSince >= TimeSpan.FromSeconds(2), "The ticker should quiesce after pause.");

            var pausedCount = TickCount(host);
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            var stillPausedCount = TickCount(host);
            Assert.AreEqual(pausedCount, stillPausedCount, "No timer fires while paused once the in-flight window has drained.");

            host.Control.Resume();
            Assert.IsFalse(host.Control.IsPaused);

            var resumed = await PollAsync(() => TickCount(host) > stillPausedCount, TimeSpan.FromSeconds(10));
            Assert.IsTrue(resumed, "The held schedule must replay on resume — the self-rescheduling chain survives the pause.");
        }

        [TestMethod]
        public async Task Pause_KeepsPropertyWritesWorking()
        {
            // The world stands still but stays pokeable: message processing continues while paused.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            host.Control.Pause();
            await host.Control.SetPropertyAsync("counter", "Counter", 77);
            Assert.AreEqual(77, host.Control.GetProperty("counter", "Counter"), "Property writes must apply while paused.");
            host.Control.Resume();
        }

        [TestMethod]
        public async Task ResetEndpoint_OnUnsupervisedHost_Returns409()
        {
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status"));
            Assert.IsFalse(status.RootElement.GetProperty("canReset").GetBoolean(), "No supervisor attached — canReset must be false.");

            var response = await client.PostAsync("/api/control/reset", null);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, "Reset on an unsupervised host must say so instead of silently doing nothing.");

            // Pause/resume round-trip over the API while we have the host up.
            await client.PostAsync("/api/control/pause", null);
            status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status"));
            Assert.IsTrue(status.RootElement.GetProperty("paused").GetBoolean());
            await client.PostAsync("/api/control/resume", null);
            status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status"));
            Assert.IsFalse(status.RootElement.GetProperty("paused").GetBoolean());
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task ClockModeEndpoint_OnUnsupervisedHost_Returns409WithReason()
        {
            // An unsupervised host (built with WithConfiguration, no host factory → CanReset false) must
            // reject a clock-mode switch with 409 + reason "notSupervised" (RFC 0012 §4).
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status"));
            Assert.IsFalse(status.RootElement.GetProperty("canReset").GetBoolean(), "No supervisor attached — canReset must be false.");

            var response = await client.PostAsync("/api/control/clock-mode?stepped=false", null);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, "Clock-mode switch on an unsupervised host must return 409.");
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("notSupervised", body.GetProperty("reason").GetString(), "409 must carry reason 'notSupervised'.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task ClockModeEndpoint_OnSupervisedHost_Returns202AndRecycles()
        {
            // A supervised host must accept a clock-mode switch with 202 + { recycling, stepped } and
            // rebuild (RFC 0012 §4). We switch from stepped to real and confirm the next generation is real.
            var port = FreePort();
            var generations = 0;

            IDevHost Factory(string? _)
            {
                generations++;
                var config = DevConfigurationBuilder.Create().WithTopologyName("clock-topo").AddLogicBlock<CounterBlock>("counter").Build();
                // Generation 1 is stepped; after the clock-mode switch the env var is set to "0" and
                // DevHostBuilderExtensions.WithWebUi reads it — so generation 2 is real.
                var builder = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port);
                return builder.Build();
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.SteppedEnvVar, "1");
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var cts = new CancellationTokenSource();
            Task runner;
            try
            {
                runner = DevHostWebRunner.RunAsync(Factory, port, cts.Token);

                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };

                // Wait for the first generation to be up and supervised.
                Assert.IsTrue(await PollCanResetAsync(client, TimeSpan.FromSeconds(30)), "Generation 1 should come up supervised.");

                var response = await client.PostAsync("/api/control/clock-mode?stepped=false", null);
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, "Clock-mode switch on a supervised host must return 202.");
                var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                Assert.IsTrue(body.TryGetProperty("recycling", out var recycling) && recycling.GetBoolean(), "202 body must carry { recycling: true }.");
                Assert.IsTrue(body.TryGetProperty("stepped", out var stepped) && !stepped.GetBoolean(), "202 body must echo the requested clock mode (false = real).");

                // Wait for generation 2 to come up.
                Assert.IsTrue(await PollCanResetAsync(client, TimeSpan.FromSeconds(30)), "Generation 2 should come up after the recycle.");
                Assert.AreEqual(2, generations, "The factory must have been called twice (two generations).");
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.SteppedEnvVar, null);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            cts.Cancel();
            await runner;
        }

        private static async Task<bool> PollCanResetAsync(HttpClient client, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status")).RootElement;
                    if (status.GetProperty("canReset").GetBoolean())
                    {
                        return true;
                    }
                }
                catch
                {
                    // Host (re)starting — keep polling.
                }

                await Task.Delay(250);
            }

            return false;
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task SupervisedRunner_RecyclesTheHost_OnResetRequest_SamePort()
        {
            var port = FreePort();
            var generations = 0;

            IDevHost HostFactory()
            {
                generations++;
                var config = DevConfigurationBuilder.Create().WithTopologyName($"gen-{generations}").AddLogicBlock<CounterBlock>("counter").Build();
                return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var cts = new CancellationTokenSource();
            Task runner;
            try
            {
                runner = DevHostWebRunner.RunAsync(HostFactory, port, cts.Token);

                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };

                Assert.IsTrue(await PollHttpAsync(client, "gen-1", TimeSpan.FromSeconds(30)), "Generation 1 should come up.");

                var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status"));
                Assert.IsTrue(status.RootElement.GetProperty("canReset").GetBoolean(), "The supervised runner must attach a reset handler.");

                var response = await client.PostAsync("/api/control/reset", null);
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

                Assert.IsTrue(await PollHttpAsync(client, "gen-2", TimeSpan.FromSeconds(30)), "Generation 2 should come up on the SAME port after the recycle.");
                Assert.AreEqual(2, generations, "The factory must have been invoked once per generation.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            cts.Cancel();
            await runner;
        }

        private static int TickCount(IDevHost host)
        {
            var value = host.Control.GetProperty("ticker", "Ticks");
            return value is int i ? i : 0;
        }

        private static async Task<bool> PollAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return condition();
        }

        private static async Task<bool> PollHttpAsync(HttpClient client, string expectedTopology, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var json = await client.GetStringAsync("/api/configuration");
                    if (json.Contains($"\"topologyName\":\"{expectedTopology}\"", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Host (re)starting — keep polling.
                }

                await Task.Delay(250);
            }

            return false;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}