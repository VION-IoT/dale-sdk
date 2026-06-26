using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Phase 1 of the topology-authoring feature (RFC 0013): the server must expose the interface-matching
    ///     metadata a later client phase uses to compute wiring. The introspection result already carries each
    ///     logic interface's <c>InterfaceTypeFullNames</c> + <c>MatchingInterfaceTypeFullNames</c> back-reference;
    ///     these tests pin that the DevHost's <c>/api/configuration</c> projection does not drop them.
    /// </summary>
    [TestClass]
    public class TopologyAuthoringShould
    {
        [TestMethod]
        [TestCategory("Smoke")]
        public async Task Configuration_CarriesInterfaceMatchingTypeFullNames()
        {
            // SourceBlock implements ISource, SinkBlock implements ISink — the PollLink contract makes ISource
            // and ISink each declare the other as its MatchingInterface, so introspection carries a non-empty
            // matchingInterfaceTypeFullNames on each block's interface entry.
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("matching").AddLogicBlock<SourceBlock>("source").AddLogicBlock<SinkBlock>("sink").Build();
            await using var host = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var response = await client.GetAsync("/api/configuration");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "GET /api/configuration must succeed.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            var interfaces = doc.RootElement.GetProperty("logicBlocks").EnumerateArray().SelectMany(lb => lb.GetProperty("interfaces").EnumerateArray()).ToList();

            Assert.IsNotEmpty(interfaces, "The wired Source/Sink network must expose logic-block interfaces.");

            // Every interface entry must carry both type-name lists, and at least one must have a non-empty
            // matchingInterfaceTypeFullNames (ISource <-> ISink are reciprocal matches).
            foreach (var iface in interfaces)
            {
                Assert.IsTrue(iface.TryGetProperty("interfaceTypeFullNames", out _), "Each interface entry must carry interfaceTypeFullNames: " + iface.GetRawText());
                Assert.IsTrue(iface.TryGetProperty("matchingInterfaceTypeFullNames", out _),
                              "Each interface entry must carry matchingInterfaceTypeFullNames: " + iface.GetRawText());
            }

            var anyMatch = interfaces.Any(iface => iface.GetProperty("matchingInterfaceTypeFullNames").EnumerateArray().Any(n => !string.IsNullOrEmpty(n.GetString())));
            Assert.IsTrue(anyMatch, "At least one interface entry must carry a non-empty matchingInterfaceTypeFullNames back-reference.");

            // And the actual back-reference must round-trip: the Source's ISource interface must name ISink as its match.
            var sourceInterface = interfaces.FirstOrDefault(iface => iface.GetProperty("identifier").GetString() == "ISource");
            Assert.AreNotEqual(JsonValueKind.Undefined, sourceInterface.ValueKind, "The Source block must expose an ISource interface entry.");
            var sourceMatches = sourceInterface.GetProperty("matchingInterfaceTypeFullNames").EnumerateArray().Select(n => n.GetString()).ToList();
            Assert.IsTrue(sourceMatches.Any(n => n is not null && n.Contains("ISink", StringComparison.Ordinal)),
                          "ISource's matchingInterfaceTypeFullNames must name ISink: " + string.Join(", ", sourceMatches));
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task LogicBlockDefinitions_Endpoint_ExposesTheCatalogWithMatchingMetadata()
        {
            // The catalog endpoint exposes every block the WithDi<> plugins register — including SourceBlock —
            // each with the per-interface matching metadata a topology-authoring client reads (RFC 0013 Phase 1).
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("matching").AddLogicBlock<SourceBlock>("source").AddLogicBlock<SinkBlock>("sink").Build();
            await using var host = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var response = await client.GetAsync("/api/logic-block-definitions");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "GET /api/logic-block-definitions must succeed.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            var definitions = doc.RootElement.GetProperty("definitions").EnumerateArray().ToList();
            Assert.IsNotEmpty(definitions, "The catalog must expose the WithDi-registered block types.");

            // The catalog must include SourceBlock by its CLR full name.
            var source = definitions.FirstOrDefault(d => d.GetProperty("typeFullName").GetString() == typeof(SourceBlock).FullName);
            Assert.AreNotEqual(JsonValueKind.Undefined, source.ValueKind, "The catalog must include a SourceBlock entry: " + typeof(SourceBlock).FullName);

            var interfaces = source.GetProperty("interfaces").EnumerateArray().ToList();
            Assert.IsNotEmpty(interfaces, "SourceBlock's catalog entry must carry its logic interfaces.");

            foreach (var iface in interfaces)
            {
                Assert.IsTrue(iface.TryGetProperty("interfaceTypeFullNames", out _), "Each interface entry must carry interfaceTypeFullNames: " + iface.GetRawText());
                Assert.IsTrue(iface.TryGetProperty("matchingInterfaceTypeFullNames", out _),
                              "Each interface entry must carry matchingInterfaceTypeFullNames: " + iface.GetRawText());
            }

            // ISource's match must round-trip to ISink — the same back-reference the wired /api/configuration carries.
            var sourceInterface = interfaces.Single(iface => iface.GetProperty("identifier").GetString() == "ISource");
            var sourceMatches = sourceInterface.GetProperty("matchingInterfaceTypeFullNames").EnumerateArray().Select(n => n.GetString()).ToList();
            Assert.IsTrue(sourceMatches.Any(n => n is not null && n.Contains("ISink", StringComparison.Ordinal)),
                          "ISource's matchingInterfaceTypeFullNames must name ISink: " + string.Join(", ", sourceMatches));
        }

        [TestMethod]
        public void LogicBlockDefinition_FromType_CarriesInterfaceMatchingMetadata()
        {
            // Built purely by reflection over the Type (no host build, no instantiation): the catalog DTO a
            // later client matcher reads to compute wiring. SourceBlock implements ISource, whose
            // [LogicInterface] names ISink as its MatchingInterface (the PollLink contract makes them reciprocal).
            var definition = LogicBlockDefinition.FromType(typeof(SourceBlock));

            Assert.AreEqual(typeof(SourceBlock).FullName, definition.TypeFullName, "TypeFullName must be the block's CLR full name.");
            Assert.IsNotEmpty(definition.Interfaces, "SourceBlock exposes at least its ISource logic interface.");

            foreach (var iface in definition.Interfaces)
            {
                Assert.IsNotEmpty(iface.InterfaceTypeFullNames, "Every interface entry must carry a non-empty interfaceTypeFullNames: " + iface.Identifier);
            }

            var anyMatch = definition.Interfaces.Any(i => i.MatchingInterfaceTypeFullNames.Count > 0 && i.MatchingInterfaceTypeFullNames.All(n => !string.IsNullOrEmpty(n)));
            Assert.IsTrue(anyMatch, "At least one interface (ISource) must carry a non-empty matchingInterfaceTypeFullNames back-reference.");

            // The back-reference must round-trip: the ISource entry must name ISink as its match.
            var source = definition.Interfaces.Single(i => i.Identifier == "ISource");
            Assert.IsTrue(source.MatchingInterfaceTypeFullNames.Any(n => n.Contains("ISink", StringComparison.Ordinal)),
                          "ISource's matchingInterfaceTypeFullNames must name ISink: " + string.Join(", ", source.MatchingInterfaceTypeFullNames));

            // And the declared consumer-side multiplicity must surface: SourceBlock binds ISource
            // [LogicBlockInterfaceBinding(typeof(ISource), Multiplicity = ExactlyOne)].
            Assert.AreEqual(LinkMultiplicity.ExactlyOne, source.Multiplicity, "ISource's binding declares Multiplicity = ExactlyOne.");
        }

        [TestMethod]
        public void LogicBlockDefinition_FromType_CarriesContractMatchingType()
        {
            // Pins the Contracts branch of FromType by reflection alone: GridBlock declares a contract property
            // Demand typed as IGridDemand, whose [ServiceProviderContractType("GridDemand")] is the token
            // introspection records as MatchingContractType. The binding sets no Identifier, so the property name
            // ("Demand") is the identifier. (GridBlock lives in the referenced SmokeHost project.)
            var definition = LogicBlockDefinition.FromType(typeof(SmokeHost.LogicBlocks.GridBlock));

            Assert.IsNotEmpty(definition.Contracts, "GridBlock's catalog entry must carry its IGridDemand contract.");

            var demand = definition.Contracts.Single(c => c.Identifier == "Demand");
            Assert.AreEqual("GridDemand", demand.MatchingContractType, "Demand's MatchingContractType must be the [ServiceProviderContractType] token.");
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