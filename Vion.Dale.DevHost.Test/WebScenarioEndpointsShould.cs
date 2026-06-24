using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The scenario HTTP surface (RFC 0006): discovery, raw file serving, apply/run with the
    ///     one-active-run rule, save-as-scenario confinement, and the Origin/Host guard on mutating routes.
    ///     Real Kestrel + plain HttpClient, like the rest of the web contract tests.
    /// </summary>
    [TestClass]
    public class WebScenarioEndpointsShould
    {
        private static readonly string SmokeScenario = """
                                                       {
                                                         "version": 1,
                                                         "id": "smoke",
                                                         "title": "Smoke",
                                                         "topology": "counter-topology",
                                                         "steps": [
                                                           { "label": "raise", "set": "counter.Counter", "value": 7 },
                                                           { "label": "doubled", "waitUntil": { "property": "counter.CounterDoubled", "above": 13 }, "timeoutSeconds": 10 }
                                                         ],
                                                         "watch": [ "counter.Counter" ],
                                                         "judge": [ { "text": "felt right", "spec": "AC-TEST-9" } ]
                                                       }
                                                       """;

        private static readonly string SlowScenario = """
                                                      {
                                                        "version": 1,
                                                        "id": "slow",
                                                        "topology": "counter-topology",
                                                        "steps": [ { "label": "dwell", "advance": { "seconds": 8 } } ]
                                                      }
                                                      """;

        [TestMethod]
        public async Task ServeDiscoveryRawFilesAndSchema()
        {
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "smoke.scenario.json"), SmokeScenario);
            File.WriteAllText(Path.Combine(dir, "broken.scenario.json"), """{ "version": 7 }""");

            var port = FreePort();
            await using var host = BuildWebHost(port, dir);
            await host.StartAsync();
            using var client = NewClient(port);

            var list = JsonDocument.Parse(await client.GetStringAsync("/api/scenarios")).RootElement;
            Assert.IsFalse(list.GetProperty("readOnly").GetBoolean());
            var entries = list.GetProperty("scenarios").EnumerateArray().ToList();
            Assert.HasCount(2, entries);
            var smoke = entries.Single(e => e.GetProperty("id").GetString() == "smoke");
            Assert.AreEqual("Smoke", smoke.GetProperty("title").GetString());
            Assert.AreEqual("counter-topology", smoke.GetProperty("topology").GetString());
            var broken = entries.Single(e => e.GetProperty("id").GetString() == "broken");
            StringAssert.Contains(broken.GetProperty("error").GetString(), "version must be 1");

            // Raw file, byte for byte.
            Assert.AreEqual(SmokeScenario, await client.GetStringAsync("/api/scenarios/smoke"));
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/scenarios/nope")).StatusCode);

            // The shipped generic schema.
            var schema = JsonDocument.Parse(await client.GetStringAsync("/api/scenarios/schema")).RootElement;
            StringAssert.Contains(schema.GetProperty("title").GetString(), "scenario file");
        }

        [TestMethod]
        public async Task ApplyARunAndServeItsLiveReport()
        {
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "smoke.scenario.json"), SmokeScenario);

            var port = FreePort();
            await using var host = BuildWebHost(port, dir);
            await host.StartAsync();
            using var client = NewClient(port);

            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/scenarios/smoke/run")).StatusCode);

            var apply = await client.PostAsync("/api/scenarios/smoke/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode);
            var runId = JsonDocument.Parse(await apply.Content.ReadAsStringAsync()).RootElement.GetProperty("runId").GetString();
            Assert.IsFalse(string.IsNullOrEmpty(runId));

            var report = await PollRunUntilDoneAsync(client, "smoke", TimeSpan.FromSeconds(30));
            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());
            Assert.AreEqual(runId, report.GetProperty("runId").GetString());
            Assert.IsTrue(report.GetProperty("steps").EnumerateArray().All(s => s.GetProperty("status").GetString() == "ok"), report.GetRawText());
            Assert.AreEqual("requiresHuman", report.GetProperty("judge")[0].GetProperty("status").GetString());

            // The scenario actually drove the network.
            Assert.AreEqual(7, host.Control.GetProperty("counter", "Counter"));
        }

        [TestMethod]
        public async Task RefuseASecondRunWhileActive_AndRestartOnDemand()
        {
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "slow.scenario.json"), SlowScenario);

            var port = FreePort();
            await using var host = BuildWebHost(port, dir);
            await host.StartAsync();
            using var client = NewClient(port);

            var first = await client.PostAsync("/api/scenarios/slow/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
            var firstRunId = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("runId").GetString();

            var conflict = await client.PostAsync("/api/scenarios/slow/apply", null);
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            var conflictBody = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual(firstRunId, conflictBody.GetProperty("activeRunId").GetString());

            var restart = await client.PostAsync("/api/scenarios/slow/apply?restart=true", null);
            Assert.AreEqual(HttpStatusCode.Accepted, restart.StatusCode);
            var secondRunId = JsonDocument.Parse(await restart.Content.ReadAsStringAsync()).RootElement.GetProperty("runId").GetString();
            Assert.AreNotEqual(firstRunId, secondRunId);

            var report = await PollRunUntilDoneAsync(client, "slow", TimeSpan.FromSeconds(30));
            Assert.AreEqual(secondRunId, report.GetProperty("runId").GetString(), report.GetRawText());
        }

        [TestMethod]
        public async Task BlockTopologyMismatchOnAnUnsupervisedHost()
        {
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "wrong.scenario.json"),
                              """{ "version": 1, "id": "wrong", "topology": "some-other", "steps": [ { "set": "counter.Counter", "value": 3 } ] }""");

            var port = FreePort();
            await using var host = BuildWebHost(port, dir);
            await host.StartAsync();
            using var client = NewClient(port);

            // The scenario's topology doesn't match the host, and this host has no supervisor to recycle onto
            // the right one (recycle-on-run needs DevHostWebRunner.RunAsync with a topology factory). Apply must
            // refuse loudly at the call (409) — never run against the wrong graph. There is no ?force= override.
            var response = await client.PostAsync("/api/scenarios/wrong/apply", null);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task SaveValidatedScenarios_ConfinedToTheDirectory()
        {
            var dir = NewScenarioDir();
            var port = FreePort();
            await using var host = BuildWebHost(port, dir);
            await host.StartAsync();
            using var client = NewClient(port);

            var saved = await client.PutAsync("/api/scenarios/fresh", Json("""{ "version": 1, "id": "fresh", "topology": "counter-topology" }"""));
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
            Assert.IsTrue(File.Exists(Path.Combine(dir, "fresh.scenario.json")));

            var mismatched = await client.PutAsync("/api/scenarios/other", Json("""{ "version": 1, "id": "fresh", "topology": "t" }"""));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, mismatched.StatusCode);

            var invalid = await client.PutAsync("/api/scenarios/fresh", Json("""{ "version": 1, "id": "fresh" }"""));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

            Environment.SetEnvironmentVariable(Scenarios.ScenarioStore.ReadOnlyEnvVar, "1");
            try
            {
                var readOnly = await client.PutAsync("/api/scenarios/fresh", Json("""{ "version": 1, "id": "fresh", "topology": "t" }"""));
                Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Scenarios.ScenarioStore.ReadOnlyEnvVar, null);
            }
        }

        [TestMethod]
        public async Task RejectCrossOriginMutations_ButNeverLocalOnes()
        {
            var port = FreePort();
            await using var host = BuildWebHost(port, NewScenarioDir());
            await host.StartAsync();
            using var client = NewClient(port);

            // A hostile page in the developer's own browser: cross-origin POST carries its Origin.
            using (var evil = new HttpRequestMessage(HttpMethod.Post, "/api/control/pause"))
            {
                evil.Headers.Add("Origin", "https://evil.example");
                Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(evil)).StatusCode);
            }

            // Reads stay open regardless of Origin (CORS governs what a page may READ).
            using (var read = new HttpRequestMessage(HttpMethod.Get, "/api/configuration"))
            {
                read.Headers.Add("Origin", "https://evil.example");
                Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode);
            }

            // The DevHost's own page: loopback Origin passes.
            using (var local = new HttpRequestMessage(HttpMethod.Post, "/api/control/pause"))
            {
                local.Headers.Add("Origin", $"http://localhost:{port}");
                Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(local)).StatusCode);
            }

            // Headless local tools (curl, agents) send no Origin at all.
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/control/resume", null)).StatusCode);
        }

        private static async Task<JsonElement> PollRunUntilDoneAsync(HttpClient client, string id, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            JsonElement last = default;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var response = await client.GetAsync($"/api/scenarios/{id}/run");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    last = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                    var status = last.GetProperty("status").GetString();
                    if (status != "running")
                    {
                        return last;
                    }
                }

                await Task.Delay(250);
            }

            Assert.Fail($"run '{id}' did not finish within {timeout.TotalSeconds} s: {(last.ValueKind == JsonValueKind.Undefined ? "never seen" : last.GetRawText())}");
            return last;
        }

        private static StringContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        private static string NewScenarioDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-scenarios-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static HttpClient NewClient(int port)
        {
            return new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
        }

        private static IDevHost BuildWebHost(int port, string scenariosDir)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").WithScenarios(scenariosDir).AddLogicBlock<CounterBlock>("counter").Build();
            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
        }

        // OS-assigned free port — avoids fixed-port collisions when this runs alongside the rest of the
        // solution's test assemblies in parallel (same helper as WebControlEndpointsShould).
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