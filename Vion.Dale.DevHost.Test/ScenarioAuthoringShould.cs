using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The scenario-authoring round-trip (RFC 0014): the DevHost lets a client author a scenario, persist it
    ///     (<c>PUT /api/scenarios/{id}</c>), read it back (<c>GET /api/scenarios/{id}</c>), then run it
    ///     (<c>POST /api/scenarios/{id}/apply</c> → poll <c>GET /api/scenarios/{id}/run</c>). This Tier-1 smoke pins
    ///     the full author → save → apply → succeeded path against the existing backend — a coverage/regression
    ///     guard, not red-green new code. It runs on the SmokeHost <see cref="SmokeHost.LogicBlocks.ShowcaseBlock" />
    ///     (writable <c>Setpoint</c> property + a 1 s <c>[Timer]</c> that increments <c>Cycles</c>), so under the
    ///     deterministic stepped clock a staged setpoint and an exact post-advance cycle count are both deterministic.
    /// </summary>
    [TestClass]
    public class ScenarioAuthoringShould
    {
        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Scenario_PutGetApply_RoundTrips()
        {
            var dir = NewScenarioDir();
            var port = FreePort();

            // The SmokeHost ShowcaseBlock (registered as "ShowcaseBlock") exposes the writable [ServiceProperty]
            // double Setpoint and a [Timer(1)] OnTick that increments Cycles once per virtual second. On the
            // deterministic stepped clock both assertions are exact: the staged Setpoint is read but never written
            // by the timer (so it holds at 50), and advancing 3 virtual seconds fires OnTick exactly 3 times
            // (so Cycles == 3).
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("minimal")
                                                .WithScenarios(dir)
                                                .AddLogicBlock<SmokeHost.LogicBlocks.ShowcaseBlock>("ShowcaseBlock")
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // 1) Author + save: PUT a small deterministic scenario. The setup stages the writable Setpoint; the
            // steps advance the virtual clock and then assert both the staged value and the exact timer-driven
            // cycle count.
            var body = """
                       {
                         "version": 1,
                         "id": "authored",
                         "topology": "minimal",
                         "watch": ["ShowcaseBlock.Setpoint", "ShowcaseBlock.Cycles"],
                         "setup": [ { "set": "ShowcaseBlock.Setpoint", "value": 50 } ],
                         "steps": [
                           { "advance": { "seconds": 3 } },
                           { "expect": { "property": "ShowcaseBlock.Setpoint", "equals": 50 } },
                           { "expect": { "property": "ShowcaseBlock.Cycles", "equals": 3 } }
                         ]
                       }
                       """;

            var saved = await client.PutAsync("/api/scenarios/authored", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());

            // 2) Read-back: GET must round-trip the saved scenario.
            var fetched = await client.GetAsync("/api/scenarios/authored");
            Assert.AreEqual(HttpStatusCode.OK, fetched.StatusCode, "GET /api/scenarios/authored must round-trip the saved scenario.");
            using (var fetchedDoc = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync()))
            {
                Assert.AreEqual("authored", fetchedDoc.RootElement.GetProperty("id").GetString(), "GET must serve the saved scenario's id.");
            }

            // 3) Apply: starts a run (the host is clean + on the matching topology, so it runs in place).
            var apply = await client.PostAsync("/api/scenarios/authored/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode, "Applying the authored scenario must start a run.");

            // 4) Poll the run report to a terminal status and assert success + the expect steps passed.
            var report = await PollRunUntilDoneAsync(client, "authored", TimeSpan.FromSeconds(30));
            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());
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

                await Task.Delay(200);
            }

            Assert.Fail($"run '{id}' did not finish within {timeout.TotalSeconds} s.");
            return last;
        }

        private static string NewScenarioDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-scenario-authoring-" + Guid.NewGuid().ToString("N"));
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