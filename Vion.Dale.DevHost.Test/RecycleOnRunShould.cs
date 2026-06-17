using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Recycle-on-run (RFC 0008): a scenario runs against the topology it declares, from a clean slate, so
    ///     every run is reproducible. On a supervised host a dirty (already-advanced) stepped generation
    ///     recycles onto the scenario's topology before running — apply answers <c>{ recycling: true }</c> and
    ///     the caller re-applies on the fresh, clean generation, which runs in place. There is no <c>force</c>.
    /// </summary>
    [TestClass]
    public class RecycleOnRunShould
    {
        [TestMethod]
        public async Task ApplyOnADirtySteppedHost_RecyclesToACleanSlate_ThenRunsOnReapply()
        {
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "recyclable.scenario.json"),
                              """
                              {
                                "version": 1, "id": "recyclable", "topology": "recycle-topo", "watch": ["Ticker.Ticks"],
                                "steps": [
                                  { "advance": { "seconds": 3 } },
                                  { "expect": { "property": "Ticker.Ticks", "equals": 3 } }
                                ]
                              }
                              """);

            var port = FreePort();

            IDevHost Factory(string? requestedTopology)
            {
                // A stepped, web-enabled generation on the scenario's topology, with the scenario dir wired.
                var config = DevConfigurationBuilder.Create().WithTopologyName("recycle-topo").WithScenarios(dir).AddLogicBlock<TickerBlock>("Ticker").Build();
                return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithDeterministicStepping().WithWebUi(port).Build();
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var cts = new CancellationTokenSource();
            Task runner;
            try
            {
                runner = DevHostWebRunner.RunAsync(Factory, port, cts.Token);
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };

                Assert.IsTrue(await PollSteppedReadyAsync(client, TimeSpan.FromSeconds(30)), "Generation 1 (stepped, supervised) should come up.");

                // Dirty the generation: advance the virtual clock so it is no longer at the clean baseline.
                var advance = await client.PostAsync("/api/control/advance?seconds=10", null);
                Assert.AreEqual(HttpStatusCode.OK, advance.StatusCode);

                // Apply on the dirty host must recycle (not run against leftover state) and say so.
                var apply = await client.PostAsync("/api/scenarios/recyclable/apply", null);
                Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode);
                var applyBody = JsonDocument.Parse(await apply.Content.ReadAsStringAsync()).RootElement;
                Assert.IsTrue(applyBody.TryGetProperty("recycling", out var recycling) && recycling.GetBoolean(),
                              $"A dirty stepped host must recycle before running, not run in place. Got: {applyBody.GetRawText()}");

                // The recycle rebuilds a fresh generation: the virtual clock is back at the epoch baseline.
                Assert.IsTrue(await PollVirtualTimeAtEpochAsync(client, TimeSpan.FromSeconds(30)),
                              "After the recycle the fresh generation's clock must be back at the epoch (clean slate).");

                // Re-apply on the now-clean, matching host runs in place (a runId, not another recycle).
                var reapply = await client.PostAsync("/api/scenarios/recyclable/apply", null);
                Assert.AreEqual(HttpStatusCode.Accepted, reapply.StatusCode);
                var reapplyBody = JsonDocument.Parse(await reapply.Content.ReadAsStringAsync()).RootElement;
                Assert.IsTrue(reapplyBody.TryGetProperty("runId", out _), $"A clean, matching host must run in place. Got: {reapplyBody.GetRawText()}");

                var report = await PollRunUntilDoneAsync(client, "recyclable", TimeSpan.FromSeconds(30));
                Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            cts.Cancel();
            await runner;
        }

        private static async Task<bool> PollSteppedReadyAsync(HttpClient client, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status")).RootElement;
                    if (status.GetProperty("stepped").GetBoolean() && status.GetProperty("canReset").GetBoolean())
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

        private static async Task<bool> PollVirtualTimeAtEpochAsync(HttpClient client, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status")).RootElement;
                    var virtualTime = status.GetProperty("virtualTimeUtc").GetDateTimeOffset();
                    if (virtualTime == new DateTimeOffset(2026,
                                                          1,
                                                          1,
                                                          0,
                                                          0,
                                                          0,
                                                          TimeSpan.Zero))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Host recycling — keep polling.
                }

                await Task.Delay(250);
            }

            return false;
        }

        private static async Task<JsonElement> PollRunUntilDoneAsync(HttpClient client, string id, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            JsonElement last = default;
            while (DateTime.UtcNow < deadline)
            {
                var response = await client.GetAsync($"/api/scenarios/{id}/run");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    last = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                    if (last.GetProperty("status").GetString() != "running")
                    {
                        return last;
                    }
                }

                await Task.Delay(250);
            }

            Assert.Fail($"run '{id}' did not finish within {timeout.TotalSeconds} s.");
            return last;
        }

        private static string NewScenarioDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-recycle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
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