using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Whole-DevHost smoke: boots a real web host (Kestrel + the full API/SignalR pipeline + the wired
    ///     logic-block network) and sweeps the major end-to-end paths over HTTP in one pass — the holistic
    ///     "is the assembled DevHost alive and correctly wired" check the per-feature tests don't give on
    ///     their own. Tagged <c>Smoke</c> so CI / an agent can run just this fast (
    ///     <c>dotnet test Vion.Dale.DevHost.Test --filter TestCategory=Smoke</c>). The supervised
    ///     recycle/switch/reset lifecycle is covered by the also-Smoke-tagged <see cref="RecycleOnRunShould" />
    ///     and <see cref="RunControlShould" />.
    /// </summary>
    [TestClass]
    public class DevHostSmokeShould
    {
        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Smoke_BootServeIntrospectReadWriteStepRunScenario()
        {
            var dir = NewScenarioDir();

            // A stepped scenario that runs in place on the clean, matching-topology host: stage a value,
            // advance, and assert the timer fired exactly + the staged value held.
            File.WriteAllText(Path.Combine(dir, "smoke.scenario.json"),
                              """
                              {
                                "version": 1, "id": "smoke", "topology": "smoke", "watch": ["ticker.Ticks", "counter.Counter"],
                                "setup": [ { "set": "counter.Counter", "value": 7 } ],
                                "steps": [
                                  { "advance": { "seconds": 3 } },
                                  { "expect": { "property": "ticker.Ticks", "equals": 3 } },
                                  { "expect": { "property": "counter.Counter", "equals": 7 } }
                                ]
                              }
                              """);

            var port = FreePort();
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("smoke")
                                                .WithScenarios(dir)
                                                .AddLogicBlock<CounterBlock>("counter")
                                                .AddLogicBlock<TickerBlock>("ticker")
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithDeterministicStepping().WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // 1) Boot + introspection: the wired network is described over HTTP.
            var config1 = await GetStringAsync(client, "/api/configuration");
            StringAssert.Contains(config1, "\"topologyName\":\"smoke\"", "Configuration must carry the topology name.");
            StringAssert.Contains(config1, "Counter", "Configuration must describe the Counter block's members.");

            var blocksJson = await GetStringAsync(client, "/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);
            string? counterServiceId = null;
            foreach (var block in blocksDoc.RootElement.EnumerateArray())
            {
                if (block.GetProperty("name").GetString() == "counter")
                {
                    counterServiceId = block.GetProperty("serviceIds")[0].GetString();
                }
            }

            Assert.IsFalse(string.IsNullOrEmpty(counterServiceId), "The counter block must expose a service id.");

            // 2) State read: a member value is observable.
            var stateResponse = await client.GetAsync("/api/state/counter/Counter");
            Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode, "Reading a property must succeed.");

            // 3) Writable set + read-back: a [ServiceProperty] with a public setter applies.
            var set = await client.PostAsJsonAsync($"/api/dale/property/{counterServiceId}/Counter", new { value = 42 });
            Assert.AreEqual(HttpStatusCode.OK, set.StatusCode, "Setting a writable property must succeed.");
            Assert.IsTrue(await PollPropertyAsync(client, "counter", "Counter", 42, TimeSpan.FromSeconds(10)), "The written value must become observable.");

            // 4) Read-only write is rejected loudly (not a silent 200): CounterDoubled is a measuring point.
            var readOnly = await client.PostAsJsonAsync($"/api/dale/property/{counterServiceId}/CounterDoubled", new { value = 99 });
            Assert.AreEqual(HttpStatusCode.BadRequest, readOnly.StatusCode, "Writing a read-only measuring point must fail loudly (400).");

            // 5) Stepped mode is active (deterministic virtual clock).
            var status = JsonDocument.Parse(await GetStringAsync(client, "/api/control/status")).RootElement;
            Assert.IsTrue(status.GetProperty("stepped").GetBoolean(), "WithDeterministicStepping must make the host stepped.");

            // 6) Scenario run: the host is clean and on the matching topology, so it runs in place to green.
            var apply = await client.PostAsync("/api/scenarios/smoke/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode, "Applying a matching-topology scenario on a clean host must start a run.");
            var report = await PollRunUntilDoneAsync(client, "smoke", TimeSpan.FromSeconds(30));
            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());

            // 7) Manual stepping drives the virtual clock: advancing fires the [Timer] block further.
            var ticksBefore = await GetPropertyIntAsync(client, "ticker", "Ticks");
            var advance = await client.PostAsync("/api/control/advance?seconds=2", null);
            Assert.AreEqual(HttpStatusCode.OK, advance.StatusCode, "Manual advance must succeed on a stepped host with no active run.");
            Assert.IsTrue(await PollAsync(async () => await GetPropertyIntAsync(client, "ticker", "Ticks") > ticksBefore, TimeSpan.FromSeconds(10)),
                          "Advancing the virtual clock must fire the timer further.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Smoke_HalInputScenario_DrivesTheSmokeHostIoBlock()
        {
            // Covers the serviceProviderSet / waitUntil scenario step types + the HAL round-trip in CI
            // (no other test exercises the input drive through the web apply path), and guards that the
            // SmokeHost's IoBlock boots + introspects — otherwise it's only compile-checked. Committed fixture.
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "io.scenario.json"),
                              """
                              {
                                "version": 1, "id": "io", "topology": "io", "watch": ["io.IsEnabled", "io.CurrentLevel"],
                                "steps": [
                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                  { "expect": { "property": "io.IsEnabled", "equals": true } },
                                  { "expect": { "property": "io.CurrentLevel", "equals": 3.3, "tolerance": 0.001 } }
                                ]
                              }
                              """);

            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").WithScenarios(dir).AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var apply = await client.PostAsync("/api/scenarios/io/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode, "Applying the HAL scenario must start a run.");
            var report = await PollRunUntilDoneAsync(client, "io", TimeSpan.FromSeconds(30));
            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Smoke_HalOutputScenario_AssertsTheSmokeHostIoBlockOutputs()
        {
            // The other half of HAL testing: a scenario DRIVES inputs then ASSERTS outputs. The IoBlock's
            // [Timer(1)] OnTick mirrors IsEnabled -> ActiveOutput and CurrentLevel -> EchoOutput, so after one
            // virtual-second advance the mocked outputs carry the driven values — read via the serviceProviderSet
            // / serviceProviderExpect step types and the generic output cache. No other test exercises the
            // output-assert path; this guards the full mocked-input -> block -> mocked-output loop.
            var dir = NewScenarioDir();
            File.WriteAllText(Path.Combine(dir, "io-out.scenario.json"),
                              """
                              {
                                "version": 1, "id": "io-out", "topology": "io", "watch": ["io.IsEnabled", "io.CurrentLevel"],
                                "steps": [
                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                  { "advance": { "seconds": 1 } },
                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } },
                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
                                ]
                              }
                              """);

            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").WithScenarios(dir).AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var apply = await client.PostAsync("/api/scenarios/io-out/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, apply.StatusCode, "Applying the HAL output scenario must start a run.");
            var report = await PollRunUntilDoneAsync(client, "io-out", TimeSpan.FromSeconds(30));
            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(), report.GetRawText());
        }

        private static async Task<string> GetStringAsync(HttpClient client, string path)
        {
            var response = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"GET {path} must succeed.");
            return await response.Content.ReadAsStringAsync();
        }

        private static async Task<int> GetPropertyIntAsync(HttpClient client, string block, string property)
        {
            var response = await client.GetAsync($"/api/state/{block}/{property}");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return int.MinValue;
            }

            var value = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value");
            return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.MinValue;
        }

        private static async Task<bool> PollPropertyAsync(HttpClient client, string block, string property, int expected, TimeSpan timeout)
        {
            return await PollAsync(async () => await GetPropertyIntAsync(client, block, property) == expected, timeout);
        }

        private static async Task<bool> PollAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return await condition();
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
            var dir = Path.Combine(Path.GetTempPath(), "dale-smoke-" + Guid.NewGuid().ToString("N"));
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