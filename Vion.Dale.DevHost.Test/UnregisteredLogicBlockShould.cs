using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Regression (VION-66): a topology block whose type is missing its <c>services.AddTransient&lt;T&gt;()</c>
    ///     line must stop the host at resolution with a message naming the type, the topology id and the fix.
    ///     Before the fix, <c>DevHostIntrospection.Introspect</c> logged one warning and skipped the block, which
    ///     surfaced as <see cref="System.Collections.Generic.KeyNotFoundException" /> from
    ///     <c>BuildLogicBlock</c> plus a per-tick "unknown service id" warning flood.
    /// </summary>
    [TestClass]
    public class UnregisteredLogicBlockShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.2")]
        [TestCategory("Smoke")]
        public async Task FailStartForUnregisteredBlockWiredInCode()
        {
            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("unregistered").AddLogicBlock<UnregisteredBlock>("gadget").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            var id = config.LogicBlocks[0].Id;
            // Act / Assert
            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
            StringAssert.Contains(exception.Message, id, "the message must name the topology id so the block is findable in the topology file");
            StringAssert.Contains(exception.Message, "'gadget'", "the message must name the instance");
            StringAssert.Contains(exception.Message,
                                  $"AddTransient<{typeof(UnregisteredBlock).FullName}>()",
                                  "the message must name the fix as a line that compiles where it is pasted");
            StringAssert.Contains(exception.Message, "IConfigureServices", "the message must say where the fix goes");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.2")]
        public async Task FailStartForUnregisteredBlockWiredInTopologyFile()
        {
            // The second entry path. It converges on the same GetService resolution, but a fix
            // that only guarded the fluent builder would leave this half of the bug alive.
            // Arrange
            var file = DevTopologyFile.Parse($$"""
                                               {
                                                 "id": "unregistered",
                                                 "logicBlockInstances": [
                                                   { "typeFullName": "{{typeof(UnregisteredBlock).FullName}}", "name": "gadget" }
                                                 ]
                                               }
                                               """);
            var config = DevTopologyLoader.Build(file);
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            // Act / Assert
            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.2")]
        public async Task ReportEveryUnregisteredBlockAtOnce()
        {
            // Collect-then-report, like the parser (Vion.Dale.LogicBlockParser/Program.cs): three bad blocks
            // give one message listing all three, so a topology is fixed in one pass rather than one run each.
            // Arrange
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("unregistered")
                                                .AddLogicBlock<UnregisteredBlock>("first")
                                                .AddLogicBlock<CounterBlock>("counter")
                                                .AddLogicBlock<SecondUnregisteredBlock>("second")
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            // Act / Assert
            StringAssert.Contains(exception.Message, "'first'", "the first unregistered block must be listed");
            StringAssert.Contains(exception.Message, "'second'", "the second unregistered block must be listed too — not just the first");
            Assert.IsFalse(exception.Message.Contains(nameof(CounterBlock), StringComparison.Ordinal), "a registered block must not be listed");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.3")]
        public async Task FailConfigurationReadForUnregisteredBlock()
        {
            // GetConfiguration before StartAsync self-introspects (HeadlessControlShould
            // .GetConfiguration_BeforeStart_SelfIntrospects). That path used to be where the skip surfaced as
            // KeyNotFoundException; it must now surface the same actionable failure as startup.
            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("unregistered").AddLogicBlock<UnregisteredBlock>("gadget").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => host.Control.GetConfiguration());

            // Act / Assert
            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.5")]
        public async Task StayOnRunningTopologyWhenNextOneCannotStart()
        {
            // The failure above is right for BOOT, but a topology switch reaches the same resolution from a
            // RUNNING host: DevTopologyLoader only checks that a typeFullName is loadable, so save / validate
            // / switch all accept a topology naming an unregistered block, and the recycle then fails. Taking
            // the process down there would remove the UI the operator needs to pick another topology, so the
            // supervisor must report it and recycle back onto the topology that was running.
            // Arrange
            var dir = NewTopologyDir();
            File.WriteAllText(Path.Combine(dir, "good.topology.json"), TopologyJson("good", typeof(CounterBlock), "counter"));
            File.WriteAllText(Path.Combine(dir, "broken.topology.json"), TopologyJson("broken", typeof(UnregisteredBlock), "gadget"));

            var port = FreePort();

            IDevHost Factory(string? requestedTopology)
            {
                var config = DevTopologyLoader.Load(requestedTopology ?? "good", dir);
                config.TopologiesPath = dir;
            // Act / Assert
                return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var cts = new CancellationTokenSource();
            var runner = DevHostWebRunner.RunAsync(Factory, port, cts.Token);
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
                Assert.IsTrue(await PollTopologyAsync(client, "good", TimeSpan.FromSeconds(30)), "The good topology should come up.");

                var switched = await client.PostAsync("/api/topologies/broken/switch", null);
                Assert.AreEqual(HttpStatusCode.Accepted, switched.StatusCode, await switched.Content.ReadAsStringAsync());

                // Give the recycle time to run and fail. An unguarded supervisor faults its task within
                // that window (the discriminating result); a guarded one is still waiting when it elapses.
                await Task.WhenAny(runner, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));

                Assert.IsFalse(runner.IsCompleted,
                               "A switch onto an unregistered topology must not end the supervised run: " + (runner.Exception?.GetBaseException().Message ?? "it completed"));
                Assert.IsTrue(await PollTopologyAsync(client, "good", TimeSpan.FromSeconds(30)), "The supervisor must come back on the topology that was running.");
            }
            finally
            {
                await cts.CancelAsync();
                try
                {
                    await runner;
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }

                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }
        }

        private static string TopologyJson(string id, Type blockType, string instanceName)
        {
            return $$"""
                     {
                       "id": "{{id}}",
                       "logicBlockInstances": [ { "typeFullName": "{{blockType.FullName}}", "name": "{{instanceName}}" } ]
                     }
                     """;
        }

        // Polls until /api/configuration answers with the expected topology name — the signal that a
        // generation is up and serving. Tolerates the connection refusals of a host mid-recycle.
        private static async Task<bool> PollTopologyAsync(HttpClient client, string topologyName, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var response = await client.GetAsync("/api/configuration");
                    if (response.IsSuccessStatusCode)
                    {
                        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                        if (body.TryGetProperty("topologyName", out var name) && name.GetString() == topologyName)
                        {
                            return true;
                        }
                    }
                }
                catch (HttpRequestException)
                {
                    // The host is between generations — keep polling.
                }

                await Task.Delay(200);
            }

            return false;
        }

        private static string NewTopologyDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-unregistered-" + Guid.NewGuid().ToString("N"));
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