using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Golden-file regression guard: two committed fixtures are PUT to the DevHost API, read back, and (for
    ///     the scenario) applied to verify they actually run to <c>succeeded</c>. Any change to the
    ///     authoring/save/run pipeline that silently breaks the stored shape or the runner will fail here.
    ///     <para>
    ///         <b>feature-tour.scenario.json</b> — feature-rich scenario covering all step kinds (setup set /
    ///         serviceProviderSet / waitUntil / advance / settle / expect with tolerance / struct-field expect /
    ///         expect enum / serviceProviderExpect); guards case-insensitive struct-field path resolution in
    ///         particular (the <c>expect ShowcaseBlock.HomePosition.x</c> step).
    ///     </para>
    ///     <para>
    ///         <b>feature-rig.topology.json</b> — 5-block topology with a signal-link interface mapping and 5
    ///         contract mappings; guards decision-4 (Save completes the auto-mocked contract mappings so the
    ///         saved file is self-contained rather than dependent on load-time fill).
    ///     </para>
    /// </summary>
    [TestClass]
    public class GoldenRegressionShould
    {
        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Scenario_FeatureTour_GoldenRoundTripsAndRuns()
        {
            // The feature-tour scenario targets topology "default" — boot with all 5 SmokeHost blocks
            // on that topology so apply can run in place on a clean, matching host.
            var scenariosDir = NewTempDir("dale-golden-scenario-");
            var port = FreePort();

            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("default")
                                                .WithScenarios(scenariosDir)
                                                .AddLogicBlock<SmokeHost.LogicBlocks.ShowcaseBlock>("ShowcaseBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("IoBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("GridBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.SignalSourceBlock>("SignalSourceBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.SignalSinkBlock>("SignalSinkBlock")
                                                .Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<SmokeHost.DependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .WithDeterministicStepping()
                                                 .WithWebUi(port)
                                                 .Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Read the golden file from the output directory.
            var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "feature-tour.scenario.json");
            Assert.IsTrue(File.Exists(goldenPath), $"Golden fixture not found at {goldenPath}. Ensure the csproj copies Golden/**/*.json to output.");
            var goldenJson = await File.ReadAllTextAsync(goldenPath);

            // 1) PUT the golden body — must be accepted (200).
            var putResponse = await client.PutAsync("/api/scenarios/feature-tour",
                                                    new StringContent(goldenJson, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, putResponse.StatusCode,
                            $"PUT /api/scenarios/feature-tour must accept the golden body. Response: {await putResponse.Content.ReadAsStringAsync()}");

            // 2) GET and assert logical equality with the golden.
            //    The server may normalise whitespace / field order, so compare via JsonNode.DeepEquals on
            //    re-serialised nodes (not raw text). Both sides go through the same ToString() path so
            //    formatting differences don't matter.
            var getResponse = await client.GetAsync("/api/scenarios/feature-tour");
            Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode, "GET /api/scenarios/feature-tour must return the saved scenario.");
            var getJson = await getResponse.Content.ReadAsStringAsync();

            var goldenNode = JsonNode.Parse(goldenJson);
            var fetchedNode = JsonNode.Parse(getJson);
            Assert.IsNotNull(goldenNode, "Golden JSON must parse to a non-null node.");
            Assert.IsNotNull(fetchedNode, "GET response must parse to a non-null node.");
            Assert.IsTrue(JsonNode.DeepEquals(goldenNode, fetchedNode),
                          $"GET /api/scenarios/feature-tour must round-trip the golden body exactly.\nExpected:\n{goldenNode}\n\nActual:\n{fetchedNode}");

            // 3) Apply + poll to terminal: the host is clean and on topology "default" (matching), so
            //    apply runs in place (no recycle). Assert succeeded + every step ok.
            var applyResponse = await client.PostAsync("/api/scenarios/feature-tour/apply", null);
            Assert.AreEqual(HttpStatusCode.Accepted, applyResponse.StatusCode,
                            $"POST /api/scenarios/feature-tour/apply must start a run. Response: {await applyResponse.Content.ReadAsStringAsync()}");

            var report = await PollRunUntilDoneAsync(client, "feature-tour", TimeSpan.FromSeconds(60));
            var rawReport = report.GetRawText();

            Assert.AreEqual("succeeded", report.GetProperty("status").GetString(),
                            $"feature-tour golden must run to 'succeeded'.\nRun report:\n{rawReport}");

            // Every step must also be 'ok' — a failed expect yields 'failed' on the step.
            if (report.TryGetProperty("steps", out var steps))
            {
                foreach (var step in steps.EnumerateArray())
                {
                    if (step.TryGetProperty("status", out var stepStatus))
                    {
                        Assert.AreEqual("ok", stepStatus.GetString(),
                                        $"Every step must be 'ok', but got '{stepStatus.GetString()}'.\nRun report:\n{rawReport}");
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Topology_FeatureRig_GoldenCompletesContractMappings()
        {
            // Boot with all 5 SmokeHost blocks and a writable topologies dir.
            var topologiesDir = NewTempDir("dale-golden-topology-");
            var port = FreePort();

            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("feature-rig")
                                                .WithTopologies(topologiesDir)
                                                .AddLogicBlock<SmokeHost.LogicBlocks.ShowcaseBlock>("ShowcaseBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("IoBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("GridBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.SignalSourceBlock>("SignalSourceBlock")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.SignalSinkBlock>("SignalSinkBlock")
                                                .Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<SmokeHost.DependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .WithWebUi(port)
                                                 .Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Read the golden file from the output directory.
            var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "feature-rig.topology.json");
            Assert.IsTrue(File.Exists(goldenPath), $"Golden fixture not found at {goldenPath}. Ensure the csproj copies Golden/**/*.json to output.");
            var goldenJson = await File.ReadAllTextAsync(goldenPath);

            using var goldenDoc = JsonDocument.Parse(goldenJson);
            var goldenRoot = goldenDoc.RootElement;

            // Build the PUT body: the golden with contractMappings replaced by an empty array.
            // This simulates what the editor sends (authored blocks + interface wiring, no explicit mappings).
            var goldenNode = JsonNode.Parse(goldenJson)!.AsObject();
            goldenNode["contractMappings"] = new JsonArray();
            var putBody = goldenNode.ToJsonString();

            // 1) PUT the empty-contractMappings draft — Save must complete the mappings and accept (200).
            var putResponse = await client.PutAsync("/api/topologies/feature-rig",
                                                    new StringContent(putBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, putResponse.StatusCode,
                            $"PUT /api/topologies/feature-rig must accept the draft body. Response: {await putResponse.Content.ReadAsStringAsync()}");

            // 2) GET and compare the saved topology to the golden.
            var getResponse = await client.GetAsync("/api/topologies/feature-rig");
            Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode, "GET /api/topologies/feature-rig must return the saved topology.");
            var getJson = await getResponse.Content.ReadAsStringAsync();
            using var getDoc = JsonDocument.Parse(getJson);
            var getRoot = getDoc.RootElement;

            // logicBlockInstances must equal the golden's (identity: typeFullName + name pairs).
            var goldenInstances = goldenRoot.GetProperty("logicBlockInstances");
            var getInstances = getRoot.GetProperty("logicBlockInstances");
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(goldenInstances.GetRawText()), JsonNode.Parse(getInstances.GetRawText())),
                          $"logicBlockInstances must match the golden.\nExpected:\n{goldenInstances.GetRawText()}\n\nActual:\n{getInstances.GetRawText()}");

            // interfaceMappings must equal the golden's.
            var goldenMappings = goldenRoot.GetProperty("interfaceMappings");
            var getMappings = getRoot.GetProperty("interfaceMappings");
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(goldenMappings.GetRawText()), JsonNode.Parse(getMappings.GetRawText())),
                          $"interfaceMappings must match the golden.\nExpected:\n{goldenMappings.GetRawText()}\n\nActual:\n{getMappings.GetRawText()}");

            // contractMappings must be completed to exactly the golden's 5 entries.
            var goldenContracts = goldenRoot.GetProperty("contractMappings");
            var getContracts = getRoot.GetProperty("contractMappings");

            Assert.AreEqual(5, getContracts.GetArrayLength(),
                            $"Save must complete the 5 auto-mocked contract mappings. Actual contractMappings:\n{getContracts.GetRawText()}");

            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(goldenContracts.GetRawText()), JsonNode.Parse(getContracts.GetRawText())),
                          $"contractMappings must match the golden exactly (deterministic IDs).\nExpected:\n{goldenContracts.GetRawText()}\n\nActual:\n{getContracts.GetRawText()}");
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

        private static string NewTempDir(string prefix)
        {
            var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
