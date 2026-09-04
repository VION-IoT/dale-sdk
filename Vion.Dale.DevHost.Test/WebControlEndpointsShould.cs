using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Integration smoke tests for the web surface with the control endpoints. Boots a real
    ///     DevHost with the web UI on a free port and exercises both the NEW control routes and the EXISTING
    ///     <c>/api/configuration</c> route — the latter is the first automated regression guard for the web path,
    ///     confirming the WebHostService ctor change and added endpoints didn't break the existing web UI.
    /// </summary>
    [TestClass]
    public class WebControlEndpointsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-015.1")]
        public async Task ServeConfigurationBlockListAndControlRoutes()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Existing route — regression guard for the web path.
            // Act / Assert
            var configResponse = await client.GetAsync("/api/configuration");
            Assert.AreEqual(HttpStatusCode.OK, configResponse.StatusCode, "Existing /api/configuration must still work.");
            var configBody = await configResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(configBody,
                                  "\"topologyName\":\"counter-topology\"",
                                  "/api/configuration must carry the topology name declared via WithTopologyName (the topology guard's prerequisite).");

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
        [TestProperty("spec", "AC-CTRL-001.5")]
        [TestProperty("spec", "AC-CTRL-015.3")]
        public async Task ReportSteppedModeOnControlStatus()
        {
            // Part 3: a stepped-booted web host advertises it on the control status, so the Player can show
            // the "stepped / deterministic" badge.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act / Assert
            var body = await client.GetStringAsync("/api/control/status");
            StringAssert.Contains(body, "\"stepped\":true", $"GET /api/control/status must report deterministic stepping mode. Body: {body}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-012.1")]
        [TestProperty("spec", "AC-CTRL-015.4")]
        public async Task AdvanceToNextScheduledEventOnStepRoute()
        {
            // Part 4: POST /api/control/step advances the virtual clock to the next scheduled event.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var before = await VirtualTime(client);

            // Act / Assert
            var resp = await client.PostAsync("/api/control/step", null);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var after = await VirtualTime(client);
            Assert.AreEqual(1.0, (after - before).TotalSeconds, 0.001, "POST /api/control/step must advance to the next [Timer(1)] event (+1 virtual s).");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.8")]
        [TestProperty("spec", "AC-CTRL-015.4")]
        public async Task AdvanceVirtualClockBySecondsOnAdvanceRoute()
        {
            // Part 4: POST /api/control/advance?seconds=N jumps the virtual clock, firing every event in between.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var before = await VirtualTime(client);

            // Act / Assert
            var resp = await client.PostAsync("/api/control/advance?seconds=3", null);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var after = await VirtualTime(client);
            Assert.AreEqual(3.0, (after - before).TotalSeconds, 0.001, "POST /api/control/advance?seconds=3 must move the virtual clock 3 s.");

            var stateJson = await client.GetStringAsync("/api/state/Ticker/Ticks");
            using var stateDoc = JsonDocument.Parse(stateJson);
            Assert.AreEqual(3, stateDoc.RootElement.GetProperty("value").GetInt32(), "advancing 3 virtual s must fire the [Timer(1)] exactly 3 times.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-016.4")]
        public async Task RefuseManualSteppingOnRealClockHost()
        {
            // Stepping a real-clock host is meaningless — the endpoints reject it.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act / Assert
            var step = await client.PostAsync("/api/control/step", null);
            Assert.AreEqual(HttpStatusCode.Conflict, step.StatusCode, "Manual stepping requires a stepped host.");
            var advance = await client.PostAsync("/api/control/advance?seconds=1", null);
            Assert.AreEqual(HttpStatusCode.Conflict, advance.StatusCode, "Manual stepping requires a stepped host.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.3")]
        [TestProperty("spec", "AC-CTRL-015.1")]
        public async Task ApplyWriteOverHttpAndReadItBack()
        {
            // Full HTTP write loop on the one abstraction: discover the service id, POST a JSON value to the
            // existing GUID-keyed set route, then read it back on the control state route. Exercises the
            // web → IDevHostControl → JSON-decode → actor → value-cache path end-to-end.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // The serviceId carrying Counter — CounterBlock has a single service, so serviceIds[0] holds it.
            var blocksJson = await client.GetStringAsync("/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);

            // Act / Assert
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
        [TestProperty("spec", "AC-CTRL-016.1")]
        [TestProperty("spec", "AC-CTRL-016.2")]
        public async Task RefuseWriteOverHttpNamingReasonAndMember()
        {
            // Trip wire: a write the block can't apply (read-only measuring point / unknown member) used to
            // return 200 after silently burning the 5 s ack timeout. It must now fail loudly with a 4xx so an
            // agent or developer driving the HTTP path is not misled into thinking the write took effect.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var blocksJson = await client.GetStringAsync("/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);
            var serviceId = blocksDoc.RootElement[0].GetProperty("serviceIds")[0].GetString();

            // CounterDoubled is a read-only [ServiceMeasuringPoint]. The 400 body is structured (reason +
            // property), so tooling/agents can act without string-matching the message.
            // Act / Assert
            var readOnly = await client.PostAsJsonAsync($"/api/dale/property/{serviceId}/CounterDoubled", new { value = 7 });
            Assert.AreEqual(HttpStatusCode.BadRequest, readOnly.StatusCode, "Writing a read-only member must fail loudly (400), not silently succeed (200).");
            var readOnlyBody = JsonDocument.Parse(await readOnly.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("readOnly", readOnlyBody.GetProperty("reason").GetString(), "the 400 body must name the machine-readable reason");
            Assert.AreEqual("CounterDoubled", readOnlyBody.GetProperty("property").GetString(), "the 400 body must name the offending property");

            var unknown = await client.PostAsJsonAsync($"/api/dale/property/{serviceId}/NoSuchMember", new { value = 7 });
            Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode, "Writing an unknown member must fail loudly (400).");
            var unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("unknownMember", unknownBody.GetProperty("reason").GetString());
            Assert.AreEqual("NoSuchMember", unknownBody.GetProperty("property").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.1")]
        public async Task BindConfiguredPortOnLoopbackOnly()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);

            // Act — the operating system's own listener table, so a wildcard bind cannot hide behind a request
            // that happened to originate on this machine.
            await host.StartAsync();
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(endpoint => endpoint.Port == port).ToList();

            // Assert
            Assert.IsNotEmpty(listeners, "the host must be listening on the port it was configured with");
            Assert.IsTrue(listeners.TrueForAll(endpoint => IPAddress.IsLoopback(endpoint.Address)), "listening on: " + string.Join(", ", listeners));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.7")]
        [TestProperty("spec", "AC-CTRL-016.2")]
        public async Task RefuseWriteOverHttpNobodyAcknowledged()
        {
            // The last silent shape on this route: a member that exists and takes a write, on a block that
            // never replies. It answered 200 once the window was spent, which reads as "applied".
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("rejecting").AddLogicBlock<RejectingWriteBlock>("rejector").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .WithSafetyBudgets(new DevHostBudgets { WriteAcknowledgement = TimeSpan.FromMilliseconds(200) })
                                                 .WithWebUi(port)
                                                 .Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var blocksJson = await client.GetStringAsync("/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);
            var serviceId = blocksDoc.RootElement[0].GetProperty("serviceIds")[0].GetString();

            // Act
            var refused = await client.PostAsJsonAsync($"/api/dale/property/{serviceId}/Rejected", new { value = 7 });

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
            var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("unacknowledged", body.GetProperty("reason").GetString());
            Assert.AreEqual("Rejected", body.GetProperty("property").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.3")]
        public async Task DecodeWriteToNestedComponentsMember()
        {
            // Regression for the "multi charging point has no grid effect" bug: properties living on a
            // service-bound MEMBER object (not the block type) must still decode the HTTP JSON value into
            // the CLR type. Before the fix, the undecoded JsonElement blew up in the service binder, the
            // actor swallowed the exception, and the request returned 200 after burning the full 5 s ack
            // timeout — so this test also asserts the write acks fast.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<MultiPointBlock>("multi").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // The nested service's id — identifier equals the binding member name ("PointA").
            var configJson = await client.GetStringAsync("/api/configuration");
            using var configDoc = JsonDocument.Parse(configJson);
            string? nestedServiceId = null;
            foreach (var service in configDoc.RootElement.GetProperty("logicBlocks")[0].GetProperty("services").EnumerateArray())
            {
                if (service.GetProperty("identifier").GetString() == "PointA")
                {
                    nestedServiceId = service.GetProperty("id").GetString();
                }
            }

            // Act / Assert
            Assert.IsNotNull(nestedServiceId, "The interface-bound member should surface as its own service.");

            var stopwatch = Stopwatch.StartNew();
            var setResponse = await client.PostAsJsonAsync($"/api/dale/property/{nestedServiceId}/NestedThreshold", new { value = 42.5 });
            stopwatch.Stop();
            Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(4),
                          $"The write should ack via the value-changed event, not burn the 5 s timeout (took {stopwatch.Elapsed.TotalMilliseconds:F0} ms).");

            double? value = null;
            for (var i = 0; i < 50 && value != 42.5; i++)
            {
                var stateResponse = await client.GetAsync("/api/state/multi/NestedThreshold");
                Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
                using var stateDoc = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
                var v = stateDoc.RootElement.GetProperty("value");
                if (v.ValueKind == JsonValueKind.Number)
                {
                    value = v.GetDouble();
                }

                if (value != 42.5)
                {
                    await Task.Delay(100);
                }
            }

            Assert.AreEqual(42.5, value, "The JSON value must decode against the nested member's CLR property and apply.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.4")]
        [TestProperty("spec", "AC-CTRL-008.5")]
        public async Task ResolveBareAndDottedNamesOnStateRoute()
        {
            // DF-47 over HTTP: on a block whose root service and its nested components share a member name,
            // GET /api/state/{block}/{prop} must return the ROOT value for the bare name, and must route a
            // dotted "{component}.{member}" path to that component. The reported symptom was a 0 / null read
            // here on a working device.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<RootNestedCollisionBlock>("collide").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Drive PointA to a distinctive value via its own (nested) service id.
            var pointAServiceId = await NestedServiceId(client, "PointA");

            // Act / Assert
            var setResponse = await client.PostAsJsonAsync($"/api/dale/property/{pointAServiceId}/SharedPower", new { value = 11.0 });
            Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);

            // Bare name → the ROOT service's -40 (emitted from Ready), not a component's default 0.
            var bare = await PollStateDouble(client, "/api/state/collide/SharedPower", -40.0);
            Assert.AreEqual(-40.0, bare, "A bare read of the shared name must return the ROOT service's value over HTTP.");

            // Dotted "PointA.SharedPower" → PointA's value, not null.
            var dotted = await PollStateDouble(client, "/api/state/collide/PointA.SharedPower", 11.0);
            Assert.AreEqual(11.0, dotted, "A dotted service.member path must resolve the named component over HTTP.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.5")]
        public async Task EmitDurationsAndEnumsInTheirWireForm()
        {
            // Regression for the "cannot write any TimeSpan property" bug. The UI submits the .NET TimeSpan
            // form ("00:00:05") — that must succeed (write tolerance), and the value must read back as the
            // ISO-8601 duration the codec/MQTT contract uses ("PT5S"), not the .NET form. Read and write both
            // match the codec.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var blocksJson = await client.GetStringAsync("/api/logicblocks");
            using var blocksDoc = JsonDocument.Parse(blocksJson);
            var serviceId = blocksDoc.RootElement[0].GetProperty("serviceIds")[0].GetString();

            // The .NET TimeSpan form the web UI submits — must not 500.
            // Act / Assert
            var setResponse = await client.PostAsJsonAsync($"/api/dale/property/{serviceId}/ControlInterval", new { value = "00:00:05" });
            Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode, "Posting the .NET TimeSpan form must succeed (write tolerance).");

            // Read back: the wire form must be the codec's ISO-8601 duration, not the .NET form.
            string? wire = null;
            for (var i = 0; i < 50 && wire != "PT5S"; i++)
            {
                var stateResponse = await client.GetAsync("/api/state/counter/ControlInterval");
                Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
                using var stateDoc = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
                var v = stateDoc.RootElement.GetProperty("value");
                if (v.ValueKind == JsonValueKind.String)
                {
                    wire = v.GetString();
                }

                if (wire != "PT5S")
                {
                    await Task.Delay(100);
                }
            }

            Assert.AreEqual("PT5S", wire, "A TimeSpan must read back as an ISO-8601 duration on the wire (codec/MQTT canonical), not the .NET form.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-018.1")]
        [TestProperty("spec", "AC-CTRL-018.2")]
        public async Task PrimeConnectingClientWithCurrentState()
        {
            // The live web UI relies on the SignalR hub priming a freshly connected client. Collapsing the state
            // provider moved that prime onto IDevHostControl (hub.OnConnectedAsync -> control.PublishAllStates ->
            // broadcaster -> client). HTTP route tests can't reach this; a real SignalR client can. This guards
            // the exact path a browser exercises on (re)connect.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            var connection = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/hub").Build();

            var primed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.On<JsonElement>("PropertyValueChanged",
                                       payload =>
                                       {
                                           // Any PropertyValueChanged for Counter confirms the
                                           // prime-on-connect broadcast reached this client.
                                           if (payload.TryGetProperty("propertyIdentifier", out var pid) && pid.GetString() == "Counter")
                                           {
                                               primed.TrySetResult(pid.GetString());
                                           }
                                       });

            try
            {
                await connection.StartAsync();

                var completed = await Task.WhenAny(primed.Task, Task.Delay(TimeSpan.FromSeconds(15)));

                // Act / Assert
                Assert.AreEqual(primed.Task, completed, "A connected SignalR client should be primed with state on connect.");
                Assert.AreEqual("Counter", await primed.Task);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.4")]
        public async Task ServeVendoredAssetsFromEmbeddedResources()
        {
            // R0 self-containment: the UI must work offline. The runtime JS dependencies are vendored as
            // embedded static assets (signalr + dayjs and its plugins), and index.html must not load
            // anything from a CDN — the regression this test locks out is reintroducing a CDN script tag.
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            foreach (var asset in new[]
                                  {
                                      "/signalr.min.js",
                                      "/dayjs.min.js",
                                      "/dayjs.relativeTime.min.js",
                                      "/dayjs.duration.min.js",
                                      "/dayjs.localizedFormat.min.js",
                                      "/vue.esm-browser.prod.js",
                                      "/app.js",
                                      "/app.css",
                                      "/tokens.css",
                                      "/MonaSans-Variable.ttf",
                                      "/store.js",
                                      "/components.js",
                                      "/format.js",
                                      "/wiring.js",
                                      "/predicates.js",
                                      "/predicate-conformance.json",
                                      "/THIRD-PARTY-NOTICES.txt",
                                  })
            {
                // Act / Assert
                var response = await client.GetAsync(asset);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Vendored asset {asset} must be served from the embedded wwwroot.");
            }

            var indexHtml = await client.GetStringAsync("/");
            Assert.IsFalse(indexHtml.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase), "index.html must not reference a CDN — the DevHost UI has to work offline.");
            Assert.IsFalse(indexHtml.Contains("https://", StringComparison.OrdinalIgnoreCase), "index.html must not load any external resource at runtime.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.4")]
        public async Task ForceRevalidationOnEverySpaAsset()
        {
            // The SPA ships as embedded static files under stable, non-content-hashed URLs (/components.js,
            // /index.html, …) — the no-build discipline rules out content-hashed filenames. Without a
            // Cache-Control header a browser applies heuristic freshness and reuses the OLD file after a
            // NuGet upgrade of this package, so the DevHost UI stays stale until a manual hard reload (the
            // "I upgraded but ＋new is missing" report). Serving with `no-cache` forces revalidation — still
            // a cheap 304 via the ETag — so an upgraded package's UI is picked up on the next load. This
            // covers the static-file path (/components.js, /) and the SPA fallback (a client-route path).
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            foreach (var asset in new[] { "/components.js", "/", "/some-client-route" })
            {
                // Act / Assert
                var response = await client.GetAsync(asset);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{asset} must be served (static asset or SPA fallback).");
                Assert.IsNotNull(response.Headers.CacheControl,
                                 $"{asset} must carry a Cache-Control header so the browser revalidates after a package upgrade instead of serving a stale SPA.");
                Assert.IsTrue(response.Headers.CacheControl!.NoCache,
                              $"{asset} must be served with Cache-Control: no-cache (revalidate); otherwise an upgraded DevHost package serves stale UI from the browser cache. Got: {response.Headers.CacheControl}");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.2")]
        [TestProperty("spec", "AC-CTRL-006.9")]
        public async Task PrintReadinessLineInHeadlessMode()
        {
            // Arrange
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

            // Act / Assert
            StringAssert.Contains(captured.ToString(), "\"ready\":true", "Headless mode should print a JSON readiness line.");
            StringAssert.Contains(captured.ToString(), $"\"port\":{port}", "Readiness line should include the port.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.4")]
        [TestProperty("spec", "AC-CTRL-006.6")]
        [TestProperty("spec", "AC-CTRL-006.7")]
        [TestProperty("spec", "AC-CTRL-019.4")]
        public async Task ExportThenExitStoppingHostExactlyOnce()
        {
            // DF-08: the topology-aware supervised factory holds the host in `await using`; its export branch
            // must NOT also call StopAsync explicitly, or DisposeAsync stops an already-disposed host and
            // throws ObjectDisposedException from WebHostService.StopAsync. DF-13: the export receipt must be
            // valid JSON whose path round-trips (no hand-rolled backslash escaping).
            // Arrange
            var port = FreePort();
            var exportPath = Path.Combine(Path.GetTempPath(), $"dale-export-{Guid.NewGuid():N}.json");
            CountingHost? built = null;

            var originalOut = Console.Out;
            var captured = new StringWriter();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Environment.SetEnvironmentVariable(DevHostWebRunner.ExportConfigEnvVar, exportPath);
            Console.SetOut(captured);
            try
            {
                await DevHostWebRunner.RunAsync(_ => built = new CountingHost(BuildWebHost(port)), port);
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
                Environment.SetEnvironmentVariable(DevHostWebRunner.ExportConfigEnvVar, null);
                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }
            }

            // Act / Assert
            Assert.IsNotNull(built);
            Assert.AreEqual(0, built!.ExplicitStops, "Export relies on `await using` dispose — RunAsync must not call StopAsync explicitly on the export branch (DF-08).");
            Assert.AreEqual(1, built.Disposes, "The host must be disposed exactly once on the export path.");

            var receiptLine = captured.ToString().Split('\n').Select(l => l.Trim()).Last(l => l.Contains("\"exported\""));
            using var receipt = JsonDocument.Parse(receiptLine);
            Assert.AreEqual(exportPath,
                            receipt.RootElement.GetProperty("exported").GetString(),
                            "The export receipt must round-trip the exact path through JSON — no doubled or broken escaping (DF-13).");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.5")]
        public async Task DriveContractOverHttpObservableInBlockState()
        {
            // POST /api/contracts/drive is the one manual-drive endpoint behind the SPA's HAL
            // controls — generic over every value contract, with no type-specific routes. Drive the SmokeHost
            // IoBlock's digital + analog inputs over HTTP (each addressed by its stand-in handler name from the
            // configuration's contractHandlerActorName annotation), advance the stepped clock to quiesce, then
            // read the block's driven state back over HTTP. The mocked-output read-back is covered headlessly by
            // ReadServiceProviderOutput.
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
            await host.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            var enable = await Mapping(client, "EnableInput");
            var level = await Mapping(client, "LevelInput");
            var enableHandler = await HandlerName(client, "EnableInput");
            var levelHandler = await HandlerName(client, "LevelInput");

            // Drive the inputs over the generic endpoint, then advance one virtual second to quiesce.
            // Act / Assert
            Assert.AreEqual(HttpStatusCode.OK,
                            (await client.PostAsJsonAsync($"/api/contracts/drive/{enableHandler}/{enable.Sp}/{enable.Svc}/{enable.Contract}", new { value = true })).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK,
                            (await client.PostAsJsonAsync($"/api/contracts/drive/{levelHandler}/{level.Sp}/{level.Svc}/{level.Contract}", new { value = 3.3 })).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync("/api/control/advance?seconds=1", null)).StatusCode);

            var enabled = JsonDocument.Parse(await client.GetStringAsync("/api/state/io/IsEnabled")).RootElement;
            Assert.IsTrue(enabled.GetProperty("value").GetBoolean(), "The EnableInput drive must reach the block (IsEnabled=true).");

            var currentLevel = JsonDocument.Parse(await client.GetStringAsync("/api/state/io/CurrentLevel")).RootElement;
            Assert.AreEqual(3.3, currentLevel.GetProperty("value").GetDouble(), 0.001, "The LevelInput drive must reach the block (CurrentLevel=3.3).");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-016.1")]
        [TestProperty("spec", "AC-CTRL-016.3")]
        public async Task RefuseDriveOverHttpThatWouldReachNothing()
        {
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            var enable = await Mapping(client, "EnableInput");
            var handler = await HandlerName(client, "EnableInput");

            // Act
            var unknownHandler = await client.PostAsJsonAsync($"/api/contracts/drive/NoSuchHandler/{enable.Sp}/{enable.Svc}/{enable.Contract}", new { value = true });
            var unknownContract = await client.PostAsJsonAsync($"/api/contracts/drive/{handler}/{enable.Sp}/{enable.Svc}/NoSuchContract", new { value = true });

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, unknownHandler.StatusCode);
            Assert.AreEqual("unknownHandler", JsonDocument.Parse(await unknownHandler.Content.ReadAsStringAsync()).RootElement.GetProperty("reason").GetString());
            Assert.AreEqual(HttpStatusCode.BadRequest, unknownContract.StatusCode);
            Assert.AreEqual("unknownContract", JsonDocument.Parse(await unknownContract.Content.ReadAsStringAsync()).RootElement.GetProperty("reason").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-015.2")]
        [DataRow("/api/state/NoSuchBlock", DisplayName = "an unknown block")]
        [DataRow("/api/state/NoSuchBlock/Counter", DisplayName = "a member of an unknown block")]
        [DataRow("/api/state/counter/NoSuchMember", DisplayName = "an unknown member of a known block")]
        [DataRow("/api/state/counter/NoSuchService.Counter", DisplayName = "an unknown service of a known block")]
        public async Task AnswerNotFoundForStateReadHostCannotResolve(string route)
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act / Assert
            var response = await client.GetAsync(route);

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.3")]
        public async Task AnswerNotFoundForRouteItDoesNotServe()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act / Assert
            var response = await client.GetAsync("/api/control/statuss");

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreNotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-015.5")]
        [DataRow("NaN")]
        [DataRow("Infinity")]
        [DataRow("-Infinity")]
        [DataRow("1e308")]
        [DataRow("4294968")]
        [DataRow("0")]
        [DataRow("-1")]
        public async Task RefuseManualAdvanceBeyondWhatClockCanWait(string seconds)
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildSteppedWebHost(port);
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act / Assert
            var response = await client.PostAsync($"/api/control/advance?seconds={seconds}", null);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-016.1")]
        [TestProperty("spec", "AC-CTRL-016.5")]
        public async Task CarryReasonOnTopologySwitchConflict()
        {
            // Arrange
            var directory = Path.Combine(Path.GetTempPath(), "dale-topologies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "other.topology.json"),
                              """{ "id": "other", "logicBlockInstances": [ { "name": "counter", "typeFullName": "Vion.Dale.DevHost.Test.CounterBlock" } ] }""");
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").WithTopologies(directory).AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act
            var response = await client.PostAsync("/api/topologies/other/switch", null);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            // Assert
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, body.GetRawText());
            Assert.AreEqual("notSupervised", body.GetProperty("reason").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-003.3")]
        [TestProperty("spec", "AC-CTRL-015.3")]
        public async Task PublishBlockFailuresOnControlStatusRoute()
        {
            // Arrange
            var port = FreePort();
            var config = DevConfigurationBuilder.Create().WithTopologyName("broken").AddLogicBlock<FailingConfigureBlock>("bad").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

            // Act
            var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status")).RootElement;

            // Assert
            var failures = status.GetProperty("blockFailures").EnumerateArray().ToList();
            Assert.IsNotEmpty(failures, "the status route is where an agent learns the host started over a block that did not");
            Assert.AreEqual("bad", failures[0].GetProperty("logicBlock").GetString());
            StringAssert.Contains(failures[0].GetProperty("error").GetString()!, FailingConfigureBlock.FailureMessage);
        }

        // The service id of the (single) block's nested interface-bound component (identifier == the binding
        // member name), read from /api/configuration — the addressing the set route uses.
        private static async Task<string> NestedServiceId(HttpClient client, string serviceIdentifier)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/configuration"));
            foreach (var service in doc.RootElement.GetProperty("logicBlocks")[0].GetProperty("services").EnumerateArray())
            {
                if (service.GetProperty("identifier").GetString() == serviceIdentifier)
                {
                    return service.GetProperty("id").GetString()!;
                }
            }

            throw new InvalidOperationException($"No service '{serviceIdentifier}' on the block.");
        }

        // Poll the single-property state route until its numeric "value" equals expected (or attempts elapse);
        // returns the last observed value. The startup leading edge is asynchronous, so a bare root read polls.
        private static async Task<double?> PollStateDouble(HttpClient client, string url, double expected)
        {
            double? value = null;
            for (var attempt = 0; attempt < 50 && value != expected; attempt++)
            {
                using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
                var v = doc.RootElement.GetProperty("value");
                value = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                if (value != expected)
                {
                    await Task.Delay(100);
                }
            }

            return value;
        }

        // The mocked endpoint ids for a contract, read from /api/configuration (the addressing the SPA and the
        // drive route use).
        private static async Task<(string Sp, string Svc, string Contract)> Mapping(HttpClient client, string contractIdentifier)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/configuration"));
            var io = doc.RootElement.GetProperty("logicBlocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "io");
            var mapping = io.GetProperty("contractMappings").EnumerateArray().Single(m => m.GetProperty("contractIdentifier").GetString() == contractIdentifier);
            return (mapping.GetProperty("mappedServiceProviderIdentifier").GetString()!, mapping.GetProperty("mappedServiceIdentifier").GetString()!,
                       mapping.GetProperty("mappedContractIdentifier").GetString()!);
        }

        // A contract's stand-in actor name, read from its contractHandlerActorName annotation in /api/configuration
        // — the routing key the SPA passes to POST /api/contracts/drive.
        private static async Task<string> HandlerName(HttpClient client, string contractIdentifier)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/configuration"));
            var io = doc.RootElement.GetProperty("logicBlocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "io");
            var contract = io.GetProperty("contracts").EnumerateArray().Single(c => c.GetProperty("identifier").GetString() == contractIdentifier);
            return contract.GetProperty("annotations").GetProperty("contractHandlerActorName").GetString()!;
        }

        private static async Task<DateTimeOffset> VirtualTime(HttpClient client)
        {
            var body = await client.GetStringAsync("/api/control/status");
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("virtualTimeUtc").GetDateTimeOffset();
        }

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
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
        }

        private static IDevHost BuildSteppedWebHost(int port)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(port, true).Build();
        }

        // Decorator that counts explicit StopAsync calls separately from DisposeAsync, so a test can pin
        // that the export path stops the host exactly once — via dispose, not an extra StopAsync (DF-08).
        private sealed class CountingHost : IDevHost
        {
            private readonly IDevHost _inner;

            public int ExplicitStops { get; private set; }

            public int Disposes { get; private set; }

            public CountingHost(IDevHost inner)
            {
                _inner = inner;
            }

            public IDevHostControl Control
            {
                get => _inner.Control;
            }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                return _inner.StartAsync(cancellationToken);
            }

            public Task RunAsync(CancellationToken cancellationToken = default)
            {
                return _inner.RunAsync(cancellationToken);
            }

            public Task StopAsync(CancellationToken cancellationToken = default)
            {
                ExplicitStops++;
                return _inner.StopAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                Disposes++;
                await _inner.DisposeAsync();
            }
        }
    }
}