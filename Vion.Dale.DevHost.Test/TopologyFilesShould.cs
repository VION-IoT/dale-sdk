using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
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
        [TestProperty("spec", "AC-SCEN-013.2")]
        public async Task BuildRunningNetworkFromTopologyFile()
        {
            // Arrange — Source/Sink with their PollLink contract. The interface mapping comes from the
            // FILE, not auto-discovery, so a working poll proves the declared wiring took effect.
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

            // Act
            var config = DevTopologyLoader.Build(topology);

            // Assert
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
        [TestProperty("spec", "AC-SCEN-013.3")]
        public void RejectUnresolvableTypesLoudly()
        {
            // Arrange
            var topology = DevTopologyFile.Parse("""
                                                 { "id": "broken", "logicBlockInstances": [ { "typeFullName": "No.Such.BlockType", "name": "x" } ] }
                                                 """);

            // Act / Assert
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(topology));
            StringAssert.Contains(e.Message, "No.Such.BlockType");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.10")]
        public async Task RoundTripCSharpPresetThroughExportAndLoader()
        {
            // The migration path: C# preset → export projection → file → loader → equivalent network.
            // Arrange / Act
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


            // Assert
            Assert.AreEqual("round-trip", reloaded.TopologyName);
            CollectionAssert.AreEquivalent(preset.LogicBlocks.Select(b => b.Name).ToList(), reloaded.LogicBlocks.Select(b => b.Name).ToList());
            Assert.HasCount(preset.InterfaceMappings.Count, reloaded.InterfaceMappings);
            Assert.AreEqual(preset.InterfaceMappings[0].SourceInterfaceIdentifier, reloaded.InterfaceMappings[0].SourceInterfaceIdentifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.10")]
        public void ExportConvergedContractFieldNamesAndSchemaRef()
        {
            // DF-11: the topology contract-mapping field names converged on ConfigurationOutput's `mapped*`
            // convention, and export now emits a real $schema ref (was null) so editors can validate.
            // Arrange / Act
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

            // Assert
            StringAssert.Contains(json, "\"$schema\": \"./.dale/topology.schema.json\"");
            StringAssert.Contains(json, "\"mappedServiceProviderIdentifier\": \"sp_1\"");
            StringAssert.Contains(json, "\"mappedServiceIdentifier\": \"svc_1\"");
            StringAssert.Contains(json, "\"mappedContractIdentifier\": \"C\"");

            var reparsed = DevTopologyFile.Parse(json);
            Assert.AreEqual("sp_1", reparsed.ContractMappings![0].MappedServiceProviderIdentifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.2")]
        public void RejectOldUnprefixedContractFieldNames()
        {
            // The convergence is a real (preview) break — strict parsing rejects the pre-convergence field
            // names, so a stale hand-edit fails loudly rather than silently dropping the mapping.
            // Arrange / Act
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse("""
                                                                                           {
                                                                                             "id": "demo",
                                                                                             "logicBlockInstances": [ { "typeFullName": "X.Y", "name": "a" } ],
                                                                                             "contractMappings": [ { "logicBlockName": "a", "contractIdentifier": "C", "serviceProviderIdentifier": "sp_1" } ]
                                                                                           }
                                                                                           """));

            // Assert
            StringAssert.Contains(e.Message, "serviceProviderIdentifier");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.6")]
        [TestProperty("spec", "AC-GATE-012.2")]
        public void CarryInstantiationParametersFromTopologyFileThroughBuild()
        {
            // Arrange
            // The instantiationParameters field crosses the file → model → DevConfiguration layer.
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "PointCount": 2 } }
                                                     ]
                                                   }
                                                   """);

            // Act
            var config = DevTopologyLoader.Build(topology);

            // Assert
            var station = config.LogicBlocks.Single(b => b.Name == "Station");
            Assert.IsNotNull(station.InstantiationParameters);
            Assert.AreEqual(2, station.InstantiationParameters!["PointCount"]!.GetValue<int>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.8")]
        public void RefuseTopologyNamingUnknownInstantiationParameters()
        {
            // Arrange
            // Without this the block's own fail-closed check is the only one, and it runs inside the actor
            // after the host reported itself started — so the operator sees a block with no state and no error.
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "Kount": 2, "Modell": "Plus" } }
                                                     ]
                                                   }
                                                   """);

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(topology));

            StringAssert.Contains(failure.Message, "Kount");
            StringAssert.Contains(failure.Message, "Modell");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.8")]
        public void RefuseTopologyCarryingUndecodableParameterValue()
        {
            // Arrange
            // The block decodes fail-closed, but inside the actor after the host has reported itself started.
            // The loader decodes with the same rule where the operator is: load, validate and save.
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "PointCount": "two" } }
                                                     ]
                                                   }
                                                   """);

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(topology));

            StringAssert.Contains(failure.Message, "Station");
            StringAssert.Contains(failure.Message, "PointCount");
            StringAssert.Contains(failure.Message, "cannot take");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.8")]
        public void ReportUnknownIdentifierAndUndecodableValueTogether()
        {
            // Arrange
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "Kount": 2, "PointCount": "two" } }
                                                     ]
                                                   }
                                                   """);

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(topology));

            StringAssert.Contains(failure.Message, "Kount");
            StringAssert.Contains(failure.Message, "PointCount");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.6")]
        [TestProperty("spec", "AC-GATE-012.12")]
        public void CarryNullThroughForNullableParameter()
        {
            // Arrange
            // The schema admits null only for a nullable parameter; the model and the loader pass it through,
            // and the block's decode accepts it (AC-GATE-002.7).
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "Reserve": null } }
                                                     ]
                                                   }
                                                   """);

            // Act
            var config = DevTopologyLoader.Build(topology);

            // Assert
            var parameters = config.LogicBlocks.Single(b => b.Name == "Station").InstantiationParameters!;
            Assert.IsTrue(parameters.ContainsKey("Reserve"));
            Assert.IsNull(parameters["Reserve"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.6")]
        [TestProperty("spec", "AC-GATE-012.8")]
        public void AcceptTopologyNamingDeclaredInstantiationParameters()
        {
            // The refusal reads the block type's declarations, so a correct name still loads.

            // Arrange
            var topology = DevTopologyFile.Parse($$"""
                                                   {
                                                     "id": "gated",
                                                     "logicBlockInstances": [
                                                       { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                                         "instantiationParameters": { "PointCount": 2 } }
                                                     ]
                                                   }
                                                   """);

            // Act
            var config = DevTopologyLoader.Build(topology);

            // Assert
            Assert.AreEqual(2, config.LogicBlocks.Single(b => b.Name == "Station").InstantiationParameters!["PointCount"]!.GetValue<int>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.9")]
        [TestProperty("spec", "AC-SCEN-013.7")]
        public void RoundTripInstantiationParametersThroughEditorSave()
        {
            // The editor Save re-serializes a fixed field set — instantiationParameters must survive it.
            // Arrange / Act
            var dir = Path.Combine(Path.GetTempPath(), "dale-topo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new DevTopologyStore(dir);
                var path = store.Save("gated",
                                      $$"""
                                        {
                                          "id": "gated",
                                          "logicBlockInstances": [
                                            { "typeFullName": "{{typeof(SmokeHost.LogicBlocks.GatedStationBlock).FullName}}", "name": "Station",
                                              "instantiationParameters": { "PointCount": 2 } }
                                          ]
                                        }
                                        """);

                var saved = File.ReadAllText(path);

            // Assert
                StringAssert.Contains(saved, "instantiationParameters");

                var reparsed = DevTopologyFile.Parse(saved);
                Assert.AreEqual(2, reparsed.LogicBlockInstances!.Single(i => i.Name == "Station").InstantiationParameters!["PointCount"]!.GetValue<int>());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.13")]
        public async Task ServeGenericTopologySchema()
        {
            // DF-12: the topology schema ships embedded and is served symmetrically to /api/scenarios/schema.
            // Arrange / Act
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var response = await client.GetAsync("/api/topologies/schema");


            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var schema = await response.Content.ReadAsStringAsync();
            StringAssert.Contains(schema, "logicBlockInstances");
            StringAssert.Contains(schema, "mappedServiceProviderIdentifier");

            // Not just that the field is declared: an author's editor validates against this copy, so the
            // value types have to admit the JSON null a nullable parameter takes (AC-GATE-012.12). A field
            // name assertion passes on the pre-widening schema that refused it.
            var parameterTypes =
                JsonNode.Parse(schema)!["properties"]!["logicBlockInstances"]!["items"]!["properties"]!["instantiationParameters"]!["additionalProperties"]!["type"]!.AsArray();
            CollectionAssert.Contains(parameterTypes.Select(type => type!.GetValue<string>()).ToArray(), "null");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task SwitchTopologiesFromWebApiRidingTheReset()
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