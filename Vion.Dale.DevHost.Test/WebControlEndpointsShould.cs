using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Integration smoke tests for the web surface with the control endpoints (RFC 0003). Boots a real
    ///     DevHost with the web UI on a free port and exercises both the NEW control routes and the EXISTING
    ///     <c>/api/configuration</c> route — the latter is the first automated regression guard for the web path,
    ///     confirming the WebHostService ctor change and added endpoints didn't break the existing web UI.
    /// </summary>
    [TestClass]
    public class WebControlEndpointsShould
    {
        private static int FreePort()
        {
            // OS-assigned free port — avoids fixed-port collisions when this runs alongside the rest of the
            // solution's test assemblies in parallel.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static IDevHost BuildWebHost(int port)
        {
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<CounterBlock>("counter")
                                                .Build();

            return DevHostBuilder.Create()
                                 .WithDi<TestDependencyInjection>()
                                 .WithConfiguration(config)
                                 .WithWebUi(port)
                                 .Build();
        }

        [TestMethod]
        public async Task ServeExistingConfigurationRoute_AndNewControlRoutes()
        {
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Existing route — regression guard for the web path.
            var configResponse = await client.GetAsync("/api/configuration");
            Assert.AreEqual(HttpStatusCode.OK, configResponse.StatusCode, "Existing /api/configuration must still work.");

            // New control routes.
            var blocksResponse = await client.GetAsync("/api/logicblocks");
            Assert.AreEqual(HttpStatusCode.OK, blocksResponse.StatusCode);
            var blocksBody = await blocksResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(blocksBody, "counter");

            // New control state route — addresses by block name (distinct from the existing GUID-keyed set route).
            var stateResponse = await client.GetAsync("/api/state/counter");
            Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);

            var logsResponse = await client.GetAsync("/api/logs/recent?max=50");
            Assert.AreEqual(HttpStatusCode.OK, logsResponse.StatusCode);

            var messagesResponse = await client.GetAsync("/api/messages?logicBlock=counter");
            Assert.AreEqual(HttpStatusCode.OK, messagesResponse.StatusCode);
        }

        [TestMethod]
        public async Task PostSetProperty_ThroughUnifiedControl_IsAppliedAndReadBack()
        {
            // Full HTTP write loop on the one abstraction: discover the service id, POST a JSON value to the
            // existing GUID-keyed set route, then read it back on the control state route. Exercises the
            // web → IDevHostControl → JSON-decode → actor → value-cache path end-to-end.
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // The serviceId carrying Counter — CounterBlock has a single service, so serviceIds[0] holds it.
            var blocksJson = await client.GetStringAsync("/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);
            var serviceId = blocksDoc.RootElement[0].GetProperty("serviceIds")[0].GetString();
            Assert.IsFalse(string.IsNullOrEmpty(serviceId), "The counter block should expose a service id.");

            var setResponse = await client.PostAsJsonAsync($"/api/dale/property/{serviceId}/Counter", new { value = 123 });
            Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode, "Setting a property via the unified control POST should succeed.");

            // The set flows through the actor, so the published value lands shortly after — poll the read-back.
            int? value = null;
            for (var i = 0; i < 50 && value != 123; i++)
            {
                var stateResponse = await client.GetAsync("/api/state/counter/Counter");
                Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
                using var stateDoc = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
                var v = stateDoc.RootElement.GetProperty("value");
                if (v.ValueKind == JsonValueKind.Number)
                {
                    value = v.GetInt32();
                }

                if (value != 123)
                {
                    await Task.Delay(100);
                }
            }

            Assert.AreEqual(123, value, "The value set via the unified control POST should be observable on the state route.");
        }

        [TestMethod]
        public async Task DevHostWebRunner_InHeadlessMode_PrintsReadinessAndDoesNotBlock()
        {
            var port = FreePort();
            var originalOut = Console.Out;
            var captured = new StringWriter();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);
            try
            {
                await using var host = BuildWebHost(port);

                // Cancels shortly after startup so RunAsync returns (it otherwise waits forever).
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await DevHostWebRunner.RunAsync(host, port, cts.Token);
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            StringAssert.Contains(captured.ToString(), "\"ready\":true", "Headless mode should print a JSON readiness line.");
            StringAssert.Contains(captured.ToString(), $"\"port\":{port}", "Readiness line should include the port.");
        }
    }
}
