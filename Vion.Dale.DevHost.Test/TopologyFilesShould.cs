using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Topology files (RFC 0006 R5): the dev-profile loader (types from loaded assemblies, explicit
    ///     interface wiring, auto-mocked contracts), the export projection as the C#-preset migration path,
    ///     and Player-driven switching riding the run-control reset.
    /// </summary>
    [TestClass]
    public class TopologyFilesShould
    {
        [TestMethod]
        public async Task BuildARunningNetworkFromATopologyFile()
        {
            // Source/Sink with their PollLink contract — the interface mapping comes from the FILE,
            // not auto-discovery, so a working poll proves the declared wiring took effect.
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "cross-block",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SourceBlock).FullName}}", "name": "source" },
                                                       { "typeFullName": "{{typeof(SinkBlock).FullName}}", "name": "sink" }
                                                     ],
                                                     "interfaceMappings": [
                                                       { "sourceLogicBlockName": "source", "sourceInterfaceIdentifier": "ISource",
                                                         "targetLogicBlockName": "sink", "targetInterfaceIdentifier": "ISink" }
                                                     ]
                                                   }
                                                   """);

            var config = DevTopologyLoader.Build(topology);
            Assert.AreEqual("cross-block", config.TopologyName);

            await using var host = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && host.Control.GetProperty("sink", "ReceivedPolls") as int? is null or 0)
            {
                await Task.Delay(100);
            }

            Assert.IsGreaterThan(0, host.Control.GetProperty("sink", "ReceivedPolls") as int? ?? 0, "the file-declared interface mapping should carry the startup poll");
        }

        [TestMethod]
        public void RejectUnresolvableTypesLoudly()
        {
            var topology = DevTopologyFile.Parse("""
                                                 { "id": "broken", "logicBlockInstances": [ { "typeFullName": "No.Such.BlockType", "name": "x" } ] }
                                                 """);
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(topology));
            StringAssert.Contains(e.Message, "No.Such.BlockType");
        }

        [TestMethod]
        public async Task RoundTripACSharpPresetThroughExportAndLoader()
        {
            // The migration path: C# preset → export projection → file → loader → equivalent network.
            var preset = DevConfigurationBuilder.Create()
                                                .WithTopologyName("round-trip")
                                                .AddLogicBlock<SourceBlock>("source", out var source)
                                                .AddLogicBlock<SinkBlock>("sink", out var sink)
                                                .Connect(source, sink)
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().WithConfiguration(preset).Build();
            await host.StartAsync();

            var exported = DevTopologyFile.FromConfiguration(host.Control.GetConfiguration());
            var reloaded = DevTopologyLoader.Build(DevTopologyFile.Parse(exported.ToJson()));

            Assert.AreEqual("round-trip", reloaded.TopologyName);
            CollectionAssert.AreEquivalent(preset.LogicBlocks.Select(b => b.Name).ToList(), reloaded.LogicBlocks.Select(b => b.Name).ToList());
            Assert.HasCount(preset.InterfaceMappings.Count, reloaded.InterfaceMappings);
            Assert.AreEqual(preset.InterfaceMappings[0].SourceInterfaceIdentifier, reloaded.InterfaceMappings[0].SourceInterfaceIdentifier);
        }

        [TestMethod]
        public void ExportEmitsConvergedContractFieldNamesAndASchemaRef()
        {
            // DF-11: the topology contract-mapping field names converged on ConfigurationOutput's `mapped*`
            // convention, and export now emits a real $schema ref (was null) so editors can validate.
            var file = new DevTopologyFile
                       {
                           Schema = DevTopologyFile.SchemaRef,
                           Id = "demo",
                           LogicBlockInstances = new[] { new TopologyLogicBlockInstance { TypeFullName = "X.Y", Name = "a" } },
                           ContractMappings = new[]
                                              {
                                                  new TopologyContractMapping
                                                  {
                                                      LogicBlockName = "a",
                                                      ContractIdentifier = "C",
                                                      MappedServiceProviderIdentifier = "sp_1",
                                                      MappedServiceIdentifier = "svc_1",
                                                      MappedContractIdentifier = "C",
                                                  },
                                              },
                       };

            var json = file.ToJson();
            StringAssert.Contains(json, "\"$schema\": \"./.dale/topology.schema.json\"");
            StringAssert.Contains(json, "\"mappedServiceProviderIdentifier\": \"sp_1\"");
            StringAssert.Contains(json, "\"mappedServiceIdentifier\": \"svc_1\"");
            StringAssert.Contains(json, "\"mappedContractIdentifier\": \"C\"");

            var reparsed = DevTopologyFile.Parse(json);
            Assert.AreEqual("sp_1", reparsed.ContractMappings![0].MappedServiceProviderIdentifier);
        }

        [TestMethod]
        public void RejectsTheOldUnprefixedContractFieldNames()
        {
            // The convergence is a real (preview) break — strict parsing rejects the pre-convergence field
            // names, so a stale hand-edit fails loudly rather than silently dropping the mapping.
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse("""
                                                                                           {
                                                                                             "id": "demo",
                                                                                             "logicBlockInstances": [ { "typeFullName": "X.Y", "name": "a" } ],
                                                                                             "contractMappings": [ { "logicBlockName": "a", "contractIdentifier": "C", "serviceProviderIdentifier": "sp_1" } ]
                                                                                           }
                                                                                           """));
            StringAssert.Contains(e.Message, "serviceProviderIdentifier");
        }

        [TestMethod]
        public async Task ServeTheGenericTopologySchema()
        {
            // DF-12: the topology schema ships embedded and is served symmetrically to /api/scenarios/schema.
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var response = await client.GetAsync("/api/topologies/schema");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var schema = await response.Content.ReadAsStringAsync();
            StringAssert.Contains(schema, "logicBlockInstances");
            StringAssert.Contains(schema, "mappedServiceProviderIdentifier");
        }

        [TestMethod]
        public async Task SwitchTopologiesFromTheWebApi_RidingTheReset()
        {
            var topologiesDir = Path.Combine(Path.GetTempPath(), "dale-topologies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(topologiesDir);
            File.WriteAllText(Path.Combine(topologiesDir, "dual.topology.json"),
                              $$"""
                                { "id": "dual", "logicBlockInstances": [ { "typeFullName": "{{typeof(DualPointBlock).FullName}}", "name": "dual" } ] }
                                """);

            var port = FreePort();

            IDevHost BuildHost(string? topologyId)
            {
                var config = topologyId is null ?
                                 DevConfigurationBuilder.Create()
                                                        .WithTopologyName("counter-topology")
                                                        .WithTopologies(topologiesDir)
                                                        .AddLogicBlock<CounterBlock>("counter")
                                                        .Build() : DevTopologyLoader.Load(topologyId, topologiesDir);
                config.TopologiesPath = topologiesDir;
                return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var shutdown = new CancellationTokenSource();
            var runner = DevHostWebRunner.RunAsync(BuildHost, port, shutdown.Token);

            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
                await PollTopologyAsync(client, "counter-topology", TimeSpan.FromSeconds(20));

                var list = await client.GetStringAsync("/api/topologies");
                StringAssert.Contains(list, "\"dual\"");
                StringAssert.Contains(list, "\"canSwitch\":true");

                var response = await client.PostAsync("/api/topologies/dual/switch", null);
                Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());

                // Generation 2 comes back on the same port running the file-built topology.
                await PollTopologyAsync(client, "dual", TimeSpan.FromSeconds(30));

                Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync("/api/topologies/nope/switch", null)).StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
                shutdown.Cancel();
                await runner;
            }
        }

        private static async Task PollTopologyAsync(HttpClient client, string expected, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            string? last = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    var configuration = await client.GetStringAsync("/api/configuration");
                    last = configuration;
                    if (configuration.Contains($"\"topologyName\":\"{expected}\""))
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Host recycling — keep polling.
                }

                await Task.Delay(250);
            }

            Assert.Fail($"topology '{expected}' did not come up in time; last configuration: {last?.Substring(0, Math.Min(200, last.Length))}");
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