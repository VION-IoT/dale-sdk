using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

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
            var sourceInterface = interfaces.First(iface => iface.GetProperty("identifier").GetString() == "ISource");
            var sourceMatches = sourceInterface.GetProperty("matchingInterfaceTypeFullNames").EnumerateArray().Select(n => n.GetString()).ToList();
            Assert.IsTrue(sourceMatches.Any(n => n is not null && n.Contains("ISink", StringComparison.Ordinal)),
                          "ISource's matchingInterfaceTypeFullNames must name ISink: " + string.Join(", ", sourceMatches));
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