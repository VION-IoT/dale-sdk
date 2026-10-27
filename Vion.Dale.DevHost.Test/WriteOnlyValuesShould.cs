using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.SmokeHost.LogicBlocks;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     What the DevHost does with a write-only service property: everything it serves outside its process
    ///     carries the redaction sentinel at each write-only position, a write that echoes the sentinel keeps the
    ///     secret it was shown, and in process the value stays as the block published it. Drives the committed
    ///     SmokeHost <see cref="ShowcaseBlock" />, which holds a write-only string and a credential struct with a
    ///     write-only member — the shape a cloud-device block holds its account in — over the routes and the push
    ///     wire the browser uses.
    /// </summary>
    [TestClass]
    public sealed class WriteOnlyValuesShould
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(15);

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.1")]
        public async Task RedactWriteOnlyPositionsOnStateRoutes()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);

            // Act
            using var block = JsonDocument.Parse(await client.GetStringAsync("/api/state/Showcase"));
            using var member = JsonDocument.Parse(await client.GetStringAsync("/api/state/Showcase/ApiKey"));

            // Assert
            var state = block.RootElement;
            Assert.AreEqual("***", state.GetProperty("apiKey").GetString(), "A browser sees what a dashboard sees from a gateway: the sentinel.");
            Assert.AreEqual("***", state.GetProperty("account").GetProperty("password").GetString(), "A write-only struct member is redacted on its own.");
            Assert.AreEqual("operator", state.GetProperty("account").GetProperty("userName").GetString(), "The struct's other members stay visible.");
            Assert.AreEqual("***", member.RootElement.GetProperty("value").GetString(), "The one-member route redacts as the block route does.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.1")]
        public async Task ServeClearedWriteOnlyValueAsNull()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);
            await host.Control.SetPropertyAsync("Showcase", nameof(ShowcaseBlock.ApiKey), null);

            // Act
            using var member = JsonDocument.Parse(await client.GetStringAsync("/api/state/Showcase/ApiKey"));

            // Assert
            Assert.AreEqual(JsonValueKind.Null, member.RootElement.GetProperty("value").ValueKind, "A cleared secret must not read as one set and hidden, so a null stays null.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.1")]
        public async Task RedactWriteOnlyPositionsOnPushWire()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            var connection = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/hub").Build();
            var apiKey = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var account = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.On<JsonElement>("PropertyValueChanged",
                                       payload =>
                                       {
                                           switch (payload.GetProperty("propertyIdentifier").GetString())
                                           {
                                               case nameof(ShowcaseBlock.ApiKey):
                                                   apiKey.TrySetResult(payload.GetProperty("value").Clone());
                                                   break;
                                               case nameof(ShowcaseBlock.Account):
                                                   account.TrySetResult(payload.GetProperty("value").Clone());
                                                   break;
                                           }
                                       });

            try
            {
                // Act — connecting primes the client with current state, through the same broadcast a change takes.
                await connection.StartAsync();
                var pushedKey = await apiKey.Task.WaitAsync(Generous);
                var pushedAccount = await account.Task.WaitAsync(Generous);

                // Assert
                Assert.AreEqual("***", pushedKey.GetString(), "The push wire leaves the process, so it carries the sentinel.");
                Assert.AreEqual("***", pushedAccount.GetProperty("password").GetString(), "A write-only struct member is pushed redacted.");
                Assert.AreEqual("operator", pushedAccount.GetProperty("userName").GetString(), "The struct's other members are pushed as they are.");
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.1")]
        public async Task RedactWriteOnlyPositionsOnCapturedMessageRoute()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);
            await host.Control.SetPropertyAsync("Showcase", nameof(ShowcaseBlock.ApiKey), "fresh-secret");

            // Act
            var messages = await client.GetStringAsync("/api/messages");

            // Assert
            StringAssert.Contains(messages, "SetServicePropertyValueRequest", "The write's own message is captured, so its absence below is not vacuous.");
            Assert.DoesNotContain("fresh-secret", messages, "A captured set, its answer and the change it causes carry the value; the route serves them redacted.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.2")]
        public async Task KeepHeldSecretWhenWriteEchoesSentinel()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);

            // Act
            var response = await client.PostAsJsonAsync($"/api/dale/property/{ServiceId(host)}/ApiKey", new { value = "***" });

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual("showcase-api-key",
                            host.Control.GetProperty("Showcase", nameof(ShowcaseBlock.ApiKey)),
                            "An editor echoes the sentinel it was shown; storing the literal would destroy the secret.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.2")]
        public async Task KeepHeldMemberSecretWhenStructWriteEchoesSentinel()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);

            // Act — the struct form primes itself from the redacted value and writes the whole struct back.
            var response = await client.PostAsJsonAsync($"/api/dale/property/{ServiceId(host)}/Account", new { value = new { userName = "admin", password = "***" } });

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(new AccountCredentials("admin", "showcase-password"),
                            host.Control.GetProperty("Showcase", nameof(ShowcaseBlock.Account)),
                            "Editing the visible member takes; the echoed sentinel keeps the held password.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-CTRL-021.2")]
        public async Task ClearSecretWhenWriteCarriesNull()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            using var client = Client(port);

            // Act
            var response = await client.PostAsJsonAsync($"/api/dale/property/{ServiceId(host)}/Account", new { value = new { userName = "operator", password = (string?)null } });

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(new AccountCredentials("operator", null),
                            host.Control.GetProperty("Showcase", nameof(ShowcaseBlock.Account)),
                            "A null at a write-only position is a clear, never an echo.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-021.3")]
        public async Task ReportWriteOnlyValueUnredactedInProcess()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildHost(port);
            await host.StartAsync();
            var observe = host.Control.WaitForAsync(e => e is ServicePropertyChanged { Property: nameof(ShowcaseBlock.ApiKey) } changed && Equals(changed.Value, "rotated-secret") ?
                                                             changed.Value : null,
                                                    Generous);

            // Act
            await host.Control.SetPropertyAsync("Showcase", nameof(ShowcaseBlock.ApiKey), "rotated-secret");

            // Assert
            Assert.AreEqual("rotated-secret", await observe, "The observation stream carries the value as the block published it.");
            Assert.AreEqual("rotated-secret",
                            host.Control.GetProperty("Showcase", nameof(ShowcaseBlock.ApiKey)),
                            "So does a read in process — which is what lets a scenario set a secret and assert it.");
        }

        private static IDevHost BuildHost(int port)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("secrets").AddLogicBlock<ShowcaseBlock>("Showcase").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
        }

        private static HttpClient Client(int port)
        {
            return new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
        }

        private static string ServiceId(IDevHost host)
        {
            return host.Control.ListLogicBlocks().Single(b => b.Name == "Showcase").ServiceIds.Single();
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