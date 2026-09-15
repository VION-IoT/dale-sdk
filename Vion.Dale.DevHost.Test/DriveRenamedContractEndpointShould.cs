using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;
using Vion.Dale.Sdk.Configuration.Contract;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A drive into a contract endpoint a topology renamed. The loader rewrites a block's mapping triple and
    ///     leaves the auto-created service providers on their generated ids, so the renamed triple exists only in
    ///     the mapping and the generated one only in <c>ServiceProviders</c>. The fixture is shaped on a
    ///     consumer's renames: readable service-provider and service ids, the contract id kept — a triple absent
    ///     from <c>ServiceProviders</c>, which is what a rename onto an existing triple would not show.
    /// </summary>
    [TestClass]
    public class DriveRenamedContractEndpointShould
    {
        private const string IoBlockType = "Vion.Dale.DevHost.SmokeHost.LogicBlocks.IoBlock";

        // IoA's EnableInput is renamed and its LevelInput left generated (a partial rename); IoB and IoC share
        // one renamed EnableInput endpoint.
        private const string Topology = $$"""
                                          {
                                            "id": "renamed",
                                            "logicBlockInstances": [
                                              { "typeFullName": "{{IoBlockType}}", "name": "IoA" },
                                              { "typeFullName": "{{IoBlockType}}", "name": "IoB" },
                                              { "typeFullName": "{{IoBlockType}}", "name": "IoC" }
                                            ],
                                            "contractMappings": [
                                              { "logicBlockName": "IoA", "contractIdentifier": "EnableInput",
                                                "mappedServiceProviderIdentifier": "sp_io_a", "mappedServiceIdentifier": "svc_io_a", "mappedContractIdentifier": "EnableInput" },
                                              { "logicBlockName": "IoB", "contractIdentifier": "EnableInput",
                                                "mappedServiceProviderIdentifier": "sp_io_shared", "mappedServiceIdentifier": "svc_io_shared", "mappedContractIdentifier": "EnableInput" },
                                              { "logicBlockName": "IoC", "contractIdentifier": "EnableInput",
                                                "mappedServiceProviderIdentifier": "sp_io_shared", "mappedServiceIdentifier": "svc_io_shared", "mappedContractIdentifier": "EnableInput" }
                                            ]
                                          }
                                          """;

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-SCEN-014.13")]
        public async Task ReachBlockFromScenarioDrivingRenamedEndpoint()
        {
            // Arrange
            var dir = Path.Combine(Path.GetTempPath(), "dale-renamed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "enable.scenario.json"),
                              """
                              {
                                "version": 1, "id": "enable", "topology": "renamed",
                                "steps": [
                                  { "serviceProviderSet": { "logicBlock": "IoA", "contract": "EnableInput" }, "value": true },
                                  { "advance": { "seconds": 1 } },
                                  { "expect": { "property": "IoA.IsEnabled", "equals": true } }
                                ]
                              }
                              """);

            var config = DevTopologyLoader.Build(DevTopologyFile.Parse(Topology));
            config.ScenariosPath = dir;
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync("enable", host.Control, dir);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, JsonSerializer.Serialize(report));
            Assert.IsTrue(host.Control.GetProperty("IoA", "IsEnabled") as bool?, "the drive must reach the block the renamed endpoint is mapped to");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-CTRL-009.5")]
        public async Task ReachBlockOverHttpDrivingRenamedEndpoint()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var (handler, sp, svc, contract) = await Endpoint(client, "IoA", "EnableInput");

            // Act
            var drive = await client.PostAsJsonAsync($"/api/contracts/drive/{handler}/{sp}/{svc}/{contract}", new { value = true });
            var advance = await client.PostAsync("/api/control/advance?seconds=1", null);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, advance.StatusCode);
            Assert.AreEqual("sp_io_a/svc_io_a/EnableInput", $"{sp}/{svc}/{contract}", "the fixture must address the renamed triple");
            Assert.AreEqual(HttpStatusCode.OK, drive.StatusCode, await drive.Content.ReadAsStringAsync());
            var enabled = JsonDocument.Parse(await client.GetStringAsync("/api/state/IoA/IsEnabled")).RootElement;
            Assert.IsTrue(enabled.GetProperty("value").GetBoolean(), "the drive must reach the block the renamed endpoint is mapped to");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-CTRL-016.3")]
        public async Task RefuseDriveOverHttpIntoGeneratedEndpointRenameLeftBehind()
        {
            // The generated triple is still in the configuration's service providers, but no mapping addresses
            // it any more — and a scenario cannot name it, since the resolver reads the final mapping.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var (handler, _, _, contract) = await Endpoint(client, "IoA", "EnableInput");
            var (_, generatedSp, generatedSvc, _) = await Endpoint(client, "IoA", "LevelInput");

            // Act
            var drive = await client.PostAsJsonAsync($"/api/contracts/drive/{handler}/{generatedSp}/{generatedSvc}/{contract}", new { value = true });

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, drive.StatusCode);
            Assert.AreEqual("unknownContract", JsonDocument.Parse(await drive.Content.ReadAsStringAsync()).RootElement.GetProperty("reason").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-SCEN-014.13")]
        public async Task ReachBlockDrivingSiblingContractRenameLeftOnGeneratedEndpoint()
        {
            // Arrange
            await using var host = BuildSteppedHost();
            await host.StartAsync();
            var io = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "IoA");
            var level = io.ContractMappings.Single(m => m.ContractIdentifier == "LevelInput");

            // Act
            await host.Control.DriveServiceProviderContractAsync(HandlerName(io, "LevelInput"),
                                                                 level.MappedServiceProviderIdentifier,
                                                                 level.MappedServiceIdentifier,
                                                                 level.MappedContractIdentifier,
                                                                 JsonSerializer.SerializeToElement(3.3));
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(3.3, (double)host.Control.GetProperty("IoA", "CurrentLevel")!, 0.001);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-SCEN-014.13")]
        public async Task ReachEveryBlockSharingRenamedEndpointWithOneDrive()
        {
            // Arrange
            await using var host = BuildSteppedHost();
            await host.StartAsync();
            var ioB = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "IoB");
            var enable = ioB.ContractMappings.Single(m => m.ContractIdentifier == "EnableInput");

            // Act
            await host.Control.DriveServiceProviderContractAsync(HandlerName(ioB, "EnableInput"),
                                                                 enable.MappedServiceProviderIdentifier,
                                                                 enable.MappedServiceIdentifier,
                                                                 enable.MappedContractIdentifier,
                                                                 JsonSerializer.SerializeToElement(true));
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            // Assert
            Assert.IsTrue(host.Control.GetProperty("IoB", "IsEnabled") as bool?);
            Assert.IsTrue(host.Control.GetProperty("IoC", "IsEnabled") as bool?);
            Assert.IsFalse(host.Control.GetProperty("IoA", "IsEnabled") as bool?, "a block on another endpoint must not see the drive");
        }

        private static IDevHost BuildSteppedHost()
        {
            return DevHostBuilder.Create()
                                 .WithDi<SmokeHost.DependencyInjection>()
                                 .WithConfiguration(DevTopologyLoader.Build(DevTopologyFile.Parse(Topology)))
                                 .WithDeterministicStepping()
                                 .Build();
        }

        private static IDevHost BuildWebHost(int port)
        {
            return DevHostBuilder.Create()
                                 .WithDi<SmokeHost.DependencyInjection>()
                                 .WithConfiguration(DevTopologyLoader.Build(DevTopologyFile.Parse(Topology)))
                                 .WithWebUi(port, true)
                                 .Build();
        }

        private static string HandlerName(ConfigurationOutput.LogicBlock block, string contractIdentifier)
        {
            return block.Contracts.Single(c => c.Identifier == contractIdentifier).Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName].ToString()!;
        }

        // A block's contract endpoint and stand-in handler, read from /api/configuration — what the SPA's toggle
        // sends.
        private static async Task<(string Handler, string Sp, string Svc, string Contract)> Endpoint(HttpClient client, string blockName, string contractIdentifier)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/configuration"));
            var block = doc.RootElement.GetProperty("logicBlocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == blockName);
            var mapping = block.GetProperty("contractMappings").EnumerateArray().Single(m => m.GetProperty("contractIdentifier").GetString() == contractIdentifier);
            var contract = block.GetProperty("contracts").EnumerateArray().Single(c => c.GetProperty("identifier").GetString() == contractIdentifier);
            return (contract.GetProperty("annotations").GetProperty("contractHandlerActorName").GetString()!, mapping.GetProperty("mappedServiceProviderIdentifier").GetString()!,
                       mapping.GetProperty("mappedServiceIdentifier").GetString()!, mapping.GetProperty("mappedContractIdentifier").GetString()!);
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