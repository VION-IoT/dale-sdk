using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The supervised runner's contract: what it prints to the process that spawned it, and what it does
    ///     when a generation cannot be built or cannot start. A topology the operator switched to can refuse in
    ///     three ways — an unregistered block, a file that is gone, one that no longer builds — and none of them
    ///     may take away the very UI the operator needs to pick another topology. A failure that <em>is</em>
    ///     terminal gets a receipt, because the readiness line is what a spawning agent waits for and it needs a
    ///     counterpart to stop waiting on.
    /// </summary>
    [TestClass]
    public class SupervisedRunnerShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.2")]
        [DataRow(false, DisplayName = "the prebuilt-host overload")]
        [DataRow(true, DisplayName = "the supervised loop")]
        public async Task PrintSameReadinessLineNamingBoundPortFromEitherEntryPoint(bool supervised)
        {
            // Arrange — the preferred port is held, so the host walks; and the runner is handed a number unrelated
            // to the host's preferred one, the way the template's runner constant and its bare WithWebUi() are.
            // A line naming either number instead of the bound one is the defect.
            var preferred = FreePort();
            using var holder = Hold(IPAddress.Loopback, preferred);
            // Below the preferred port, never inside the walk: consecutive ephemeral ports would hand it the very
            // port the walk lands on.
            var unrelated = preferred - 100;
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            Task? runner = null;
            string line;
            try
            {
                runner = supervised ? DevHostWebRunner.RunAsync(() => BuildWebHost(preferred), unrelated, shutdown.Token) :
                             DevHostWebRunner.RunAsync(BuildWebHost(preferred), unrelated, shutdown.Token);
                line = await WaitForWalkedReceiptAsync(captured, "\"ready\"", preferred);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — one shape from both entry points, and the port in it is the one the host serves on.
            var readiness = JsonDocument.Parse(line).RootElement;
            Assert.IsTrue(readiness.GetProperty("ready").GetBoolean());
            Assert.AreEqual(1, readiness.GetProperty("generation").GetInt32());
            var bound = readiness.GetProperty("port").GetInt32();
            Assert.AreNotEqual(preferred, bound);
            Assert.AreNotEqual(unrelated, bound);
            Assert.IsGreaterThan(preferred, bound);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.6")]
        [DataRow("DevHost", DisplayName = "held by another development host")]
        [DataRow("IPv4", DisplayName = "held on the IPv4 loopback only")]
        [DataRow("IPv6", DisplayName = "held on the IPv6 loopback only")]
        public async Task ServeOnNextFreePortWhenPreferredOneTaken(string heldBy)
        {
            // Arrange — a port held on one loopback family only is the case a "can I bind it" probe misjudges.
            var preferred = FreePort();
            await using var holder = await HoldAsync(heldBy, preferred);
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            Task? runner = null;
            HttpStatusCode status;
            int bound;
            try
            {
                runner = DevHostWebRunner.RunAsync(BuildWebHost(preferred), preferred, shutdown.Token);
                bound = JsonDocument.Parse(await WaitForWalkedReceiptAsync(captured, "\"ready\"", preferred)).RootElement.GetProperty("port").GetInt32();
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{bound}"), Timeout = TimeSpan.FromSeconds(10) };
                status = (await client.GetAsync("/api/control/status")).StatusCode;
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — this host serves on the port its readiness line names, above the held one.
            Assert.AreEqual(HttpStatusCode.OK, status);
            Assert.IsGreaterThan(preferred, bound);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.6")]
        public async Task RefuseToStartNamingRangeWhenEveryWalkedPortTaken()
        {
            // Arrange — the preferred port and the nineteen above it, all held.
            var (preferred, holders) = HoldRange(20);
            await using var host = BuildWebHost(preferred);

            // Act
            InvalidOperationException refusal;
            try
            {
                refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());
            }
            finally
            {
                holders.ForEach(h => h.Stop());
            }

            // Assert
            StringAssert.Contains(refusal.Message, $"from {preferred} to {preferred + 19}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.4")]
        public async Task RebindLaterGenerationOnPortFirstGenerationBound()
        {
            // Arrange — generation 1 walks past a held preferred port. The holder then lets go, so a generation
            // that walked again from the preferred port would land on a different port than the one the open
            // page and a waiting client are addressing.
            var preferred = FreePort();
            var holder = Hold(IPAddress.Loopback, preferred);
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            Task? runner = null;
            int first;
            JsonElement second;
            HttpStatusCode secondStatus;
            try
            {
                runner = DevHostWebRunner.RunAsync(() => BuildWebHost(preferred), preferred, shutdown.Token);
                first = JsonDocument.Parse(await WaitForWalkedReceiptAsync(captured, "\"ready\"", preferred)).RootElement.GetProperty("port").GetInt32();
                holder.Stop();
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{first}"), Timeout = TimeSpan.FromSeconds(10) };
                Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsync("/api/control/reset", null)).StatusCode);
                second = JsonDocument.Parse(await WaitForReceiptAsync(captured, "\"generation\":2")).RootElement;
                secondStatus = (await client.GetAsync("/api/control/status")).StatusCode;
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                holder.Stop();
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert
            Assert.IsTrue(second.GetProperty("ready").GetBoolean());
            Assert.AreEqual(first, second.GetProperty("port").GetInt32());
            Assert.AreEqual(HttpStatusCode.OK, secondStatus);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.10")]
        [TestProperty("spec", "AC-CTRL-005.6")]
        public async Task FailLaterGenerationNamingPortWhenItWasTakenDuringRecycle()
        {
            // Arrange — generation 1 walks past a held preferred port, and the second generation's factory takes the
            // port generation 1 released before handing its host over: the shape of another process binding it inside
            // the recycle window. The walk is what separates the port served on from the port the runner was given.
            var preferred = FreePort();
            using var holder = Hold(IPAddress.Loopback, preferred);
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            TcpListener? thief = null;
            using var shutdown = new CancellationTokenSource();
            var generations = 0;
            var served = 0;

            IDevHost Factory()
            {
                if (++generations == 2)
                {
                    thief = HoldWhenReleased(served);
                }

                return BuildWebHost(preferred);
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            InvalidOperationException refusal;
            Task? runner = null;
            try
            {
                runner = DevHostWebRunner.RunAsync(Factory, preferred, shutdown.Token);
                served = JsonDocument.Parse(await WaitForWalkedReceiptAsync(captured, "\"ready\"", preferred)).RootElement.GetProperty("port").GetInt32();
                using (var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{served}"), Timeout = TimeSpan.FromSeconds(10) })
                {
                    await client.PostAsync("/api/control/reset", null);
                }

                // Bounded: a generation that moved to another port serves, and its runner would never end.
                Assert.AreSame(runner, await Task.WhenAny(runner, Task.Delay(TimeSpan.FromSeconds(30))), "the recycled generation kept serving instead of failing");
                refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner);
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                thief?.Stop();
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — the generation did not move to another port; it failed, and the receipt names the port it served.
            StringAssert.Contains(refusal.Message, $"port {served}");
            var receipt = JsonDocument.Parse(ReceiptLine(captured, "\"failed\"", served)).RootElement;
            Assert.AreEqual(2, receipt.GetProperty("generation").GetInt32());
            Assert.IsFalse(captured.ToString().Split('\n').Any(l => l.Contains("\"ready\"", StringComparison.Ordinal) && l.Contains("\"generation\":2", StringComparison.Ordinal)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.5")]
        public async Task StayOnRunningTopologyWhenNextOneCannotBeBuilt()
        {
            // Arrange — the factory throws for the switched-to id, the way a deleted topology file does.
            var port = FreePort();
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            IDevHost? running = null;

            IDevHost Factory(string? topologyId)
            {
                if (topologyId == "gone")
                {
                    throw new FileNotFoundException("No topology 'gone' under the topologies directory.");
                }

                return running = BuildWebHost(port);
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            Task? runner = null;
            try
            {
                runner = DevHostWebRunner.RunAsync(Factory, port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                running!.Control.TryRequestTopologySwitch("gone");
                await WaitForLineAsync(captured, "cannot start");
                await WaitForReceiptAsync(captured, "\"generation\":3", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — the process survived and a third generation came up on the topology that was running.
            var console = captured.ToString();
            StringAssert.Contains(console, "Topology 'gone' cannot start");
            Assert.AreEqual(3, JsonDocument.Parse(ReceiptLine(captured, "\"ready\"", port)).RootElement.GetProperty("generation").GetInt32());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.6")]
        public async Task PrintFailureReceiptWhenBootGenerationCannotBeBuilt()
        {
            // Arrange — a boot generation that fails has nothing to recycle back onto, so the process ends;
            // an agent waiting on the readiness line needs to learn that from stdout, not from its own timeout.
            var port = FreePort();
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            try
            {
                await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => DevHostWebRunner.RunAsync(_ => throw new FileNotFoundException("no topology 'default'"),
                                                                                                       port,
                                                                                                       CancellationToken.None));
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert
            var receipt = JsonDocument.Parse(ReceiptLine(captured, "\"failed\"", port)).RootElement;
            Assert.IsTrue(receipt.GetProperty("failed").GetBoolean());
            Assert.AreEqual(1, receipt.GetProperty("generation").GetInt32());
            StringAssert.Contains(receipt.GetProperty("reason").GetString()!, "no topology 'default'");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.5")]
        public async Task RefuseExportTargetItCannotWrite()
        {
            // Arrange — a whitespace-only value (a shell that quoted an empty variable) and a folder that does
            // not exist both used to boot the whole network and then throw from the file write.
            var port = FreePort();
            var missingFolder = Path.Combine(Path.GetTempPath(), "dale-no-such-" + Guid.NewGuid().ToString("N"), "configuration.json");
            await using var host = BuildWebHost(port);
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Environment.SetEnvironmentVariable(DevHostWebRunner.ExportConfigEnvVar, missingFolder);

            // Act
            InvalidOperationException refusal;
            try
            {
                refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => DevHostWebRunner.RunAsync(host, port, CancellationToken.None));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
                Environment.SetEnvironmentVariable(DevHostWebRunner.ExportConfigEnvVar, null);
            }

            // Assert
            StringAssert.Contains(refusal.Message, DevHostWebRunner.ExportConfigEnvVar);
            StringAssert.Contains(refusal.Message, "does not exist");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.7")]
        public async Task StopRunningGenerationOnCancellation()
        {
            // Arrange
            var port = FreePort();
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            IDevHost? running = null;
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act — Ctrl+C during a generation must stop that host and return, not build another.
            Task? runner = null;
            try
            {
                runner = DevHostWebRunner.RunAsync(_ => running = BuildWebHost(port), port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — exactly one generation was ever announced, and the port it held is free again.
            Assert.HasCount(1, ReceiptLines(captured, "\"ready\"", port).ToList());
            Assert.IsNotNull(running);
            var rebind = new TcpListener(IPAddress.Loopback, port);
            rebind.Start();
            rebind.Stop();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.1")]
        [TestProperty("spec", "AC-CTRL-020.4")]
        [DataRow("1", true, DisplayName = "the one spelling that enables")]
        [DataRow("0", false, DisplayName = "zero")]
        [DataRow("true", false, DisplayName = "true")]
        [DataRow("TRUE", false, DisplayName = "TRUE")]
        [DataRow("yes", false, DisplayName = "yes")]
        [DataRow("", false, DisplayName = "the empty string")]
        [DataRow(null, false, DisplayName = "unset")]
        public async Task ReadEnvironmentSwitchAsEnabledOnlyForOne(string? value, bool expected)
        {
            // Arrange — the stepped switch is the one a caller can observe without a browser or a file; the
            // other five switches read it with the same comparison.
            var port = FreePort();
            Environment.SetEnvironmentVariable(DevHostWebRunner.SteppedEnvVar, value);

            // Act
            try
            {
                var configuration = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
                await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).WithWebUi(port).Build();

                // Assert
                Assert.AreEqual(expected, host.Control.IsStepped);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.SteppedEnvVar, null);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-006.8")]
        public async Task PrintBoundAddressAndScenarioDeepLinksBeforeReadiness()
        {
            // Arrange — one scenario that parses and one that does not; the operator needs both named. The
            // preferred port is held, so an address printed before the bind names the wrong one.
            var directory = Path.Combine(Path.GetTempPath(), "dale-links-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "good.scenario.json"), """{ "version": 1, "id": "good", "title": "Good", "topology": "counter-topology", "steps": [] }""");
            File.WriteAllText(Path.Combine(directory, "broken.scenario.json"), """{ "version": 7 }""");
            var preferred = FreePort();
            await using var holder = await HoldAsync("IPv4", preferred);
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").WithScenarios(directory).AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).WithWebUi(preferred).Build();
            var originalOut = Console.Out;
            var captured = new ConsoleCapture();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            Task? runner = null;
            int port;
            try
            {
                runner = DevHostWebRunner.RunAsync(host, preferred, shutdown.Token);
                port = JsonDocument.Parse(await WaitForWalkedReceiptAsync(captured, "\"ready\"", preferred)).RootElement.GetProperty("port").GetInt32();
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — both links, and both before the readiness line an agent waits for.
            var console = captured.ToString();
            StringAssert.Contains(console, $"DevHost Web UI running at http://localhost:{port}");
            StringAssert.Contains(console, $"scenario good: http://localhost:{port}/#/scenario/good");
            StringAssert.Contains(console, "scenario broken: INVALID");
            Assert.IsLessThan(console.IndexOf("\"ready\"", StringComparison.Ordinal),
                              console.IndexOf("scenario good", StringComparison.Ordinal),
                              "the deep links come before the readiness line, so an agent can read them and then wait");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-016.5")]
        [TestProperty("spec", "AC-CTRL-017.1")]
        public async Task RefuseTopologySwitchWhenSupervisorRebuildsOneGraph()
        {
            // Arrange — the supervised overload whose factory takes no topology id, so every generation is the
            // same graph. It answered "switching" and came back on the topology it was already on.
            var directory = Path.Combine(Path.GetTempPath(), "dale-topologies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "other.topology.json"),
                              $$"""
                                { "id": "other", "logicBlockInstances": [ { "typeFullName": "{{typeof(CounterBlock).FullName}}", "name": "counter" } ] }
                                """);

            var port = FreePort();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");

            Task? runner = null;
            try
            {
                runner = DevHostWebRunner.RunAsync(() => BuildWebHost(port, directory), port, shutdown.Token);

                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
                await WaitUntilServingAsync(client);

                // Act
                var refusal = await client.PostAsync("/api/topologies/other/switch", null);
                var listing = await client.GetStringAsync("/api/topologies");
                var body = await refusal.Content.ReadAsStringAsync();

                // Assert — refused with the family's token, and the listing says so before a client tries.
                Assert.AreEqual(HttpStatusCode.Conflict, refusal.StatusCode, body);
                Assert.AreEqual("notSupervised", JsonDocument.Parse(body).RootElement.GetProperty("reason").GetString());
                StringAssert.Contains(listing, "\"canSwitch\":false");

                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                await StopRunnerAsync(shutdown, runner);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        ///     Polls the host's own status route until the generation is serving. The readiness receipt would
        ///     say the same thing, but reading it means redirecting process-wide <c>Console.Out</c>, and a
        ///     sibling suite writing to the captured buffer at the same moment corrupts it.
        /// </summary>
        private static async Task WaitUntilServingAsync(HttpClient client)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    if ((await client.GetAsync("/api/control/status")).IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not bound yet.
                }

                await Task.Delay(100);
            }

            Assert.Fail("the supervised host never started serving");
        }

        /// <summary>
        ///     Cancels the runner loop and waits for it to return. It belongs in a <c>finally</c>: a runner
        ///     left looping by a failed assertion keeps writing its receipts into whatever
        ///     <c>Console.Out</c> the next test installs, and those receipts have the same shape as that
        ///     test's own.
        /// </summary>
        private static async Task StopRunnerAsync(CancellationTokenSource shutdown, Task? runner)
        {
            await shutdown.CancelAsync();
            if (runner is null)
            {
                return;
            }

            try
            {
                await runner;
            }
            catch (Exception)
            {
                // Reached only when the try block already failed, and that failure is the one worth
                // reporting. On the happy path the Act awaits this same task, so a fault of the runner's
                // own is thrown there rather than swallowed here.
            }
        }

        // Console.SetOut is process-wide, so a host another suite is still tearing down writes into this
        // test's capture too — and its readiness receipt has the same shape as this one's. Every receipt
        // carries the port it belongs to, so that is what the lines are filtered on rather than the token
        // alone. Without it this suite passes alone and fails intermittently in the full run.
        private static string ReceiptLine(ConsoleCapture captured, string token, int port)
        {
            return ReceiptLines(captured, token, port).Last();
        }

        private static IEnumerable<string> ReceiptLines(ConsoleCapture captured, string token, int port)
        {
            return captured.ToString()
                           .Split('\n')
                           .Select(l => l.Trim())
                           .Where(l => l.StartsWith('{') && l.Contains(token, StringComparison.Ordinal) && l.Contains($"\"port\":{port}", StringComparison.Ordinal));
        }

        private static async Task WaitForReceiptAsync(ConsoleCapture captured, string token, int port)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (ReceiptLines(captured, token, port).Any())
                {
                    return;
                }

                await Task.Delay(50);
            }

            Assert.Fail($"no '{token}' receipt for port {port} reached the console. Captured:{Environment.NewLine}{captured}");
        }

        // A console LINE rather than a receipt: the fallback message is prose and carries no port, so the
        // topology id it names is what makes it this test's.
        private static async Task WaitForLineAsync(ConsoleCapture captured, string token)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (captured.ToString().Contains(token, StringComparison.Ordinal))
                {
                    return;
                }

                await Task.Delay(50);
            }

            Assert.Fail($"'{token}' never reached the console. Captured:{Environment.NewLine}{captured}");
        }

        // A walked host's port is not known up front, so its receipt is found by the range the walk may reach
        // rather than by one port — still a filter, so a sibling suite's receipt for another port does not match.
        private static async Task<string> WaitForWalkedReceiptAsync(ConsoleCapture captured, string token, int preferred)
        {
            return await WaitForReceiptLineAsync(captured,
                                                 line => line.Contains(token, StringComparison.Ordinal) &&
                                                         Enumerable.Range(preferred + 1, 19).Any(p => line.Contains($"\"port\":{p},", StringComparison.Ordinal)));
        }

        private static async Task<string> WaitForReceiptAsync(ConsoleCapture captured, string token)
        {
            return await WaitForReceiptLineAsync(captured, line => line.Contains(token, StringComparison.Ordinal));
        }

        private static async Task<string> WaitForReceiptLineAsync(ConsoleCapture captured, Func<string, bool> match)
        {
            for (var attempt = 0; attempt < 300; attempt++)
            {
                var line = captured.ToString().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{') && match(l));
                if (line is not null)
                {
                    return line;
                }

                await Task.Delay(50);
            }

            Assert.Fail($"no matching receipt reached the console. Captured:{Environment.NewLine}{captured}");
            return string.Empty;
        }

        private static TcpListener Hold(IPAddress address, int port)
        {
            var listener = new TcpListener(address, port);
            listener.Start();
            return listener;
        }

        private static async Task<IAsyncDisposable> HoldAsync(string heldBy, int port)
        {
            if (heldBy == "DevHost")
            {
                var host = BuildWebHost(port);
                await host.StartAsync();
                return host;
            }

            return new ListenerHold(Hold(heldBy == "IPv4" ? IPAddress.Loopback : IPAddress.IPv6Loopback, port));
        }

        // The first port of a run of consecutive free ports, each held on the IPv4 loopback. A port another
        // process took between the probe and the hold starts the search over.
        private static (int First, List<TcpListener> Holders) HoldRange(int count)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var first = FreePort();
                if (first + count > IPEndPoint.MaxPort)
                {
                    continue;
                }

                var holders = new List<TcpListener>();
                try
                {
                    for (var port = first; port < first + count; port++)
                    {
                        holders.Add(Hold(IPAddress.Loopback, port));
                    }

                    return (first, holders);
                }
                catch (SocketException)
                {
                    holders.ForEach(h => h.Stop());
                }
            }

            Assert.Fail($"no run of {count} free consecutive ports found");
            return (0, new List<TcpListener>());
        }

        // Called from inside the supervisor's factory, after the previous generation stopped: its release of the
        // port is not synchronous with the stop, so the hold retries until the port is free.
        private static TcpListener HoldWhenReleased(int port)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                try
                {
                    return Hold(IPAddress.Loopback, port);
                }
                catch (SocketException) when (DateTimeOffset.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static IDevHost BuildWebHost(int port, string? topologiesDirectory = null)
        {
            var builder = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter");
            if (topologiesDirectory is not null)
            {
                builder = builder.WithTopologies(topologiesDirectory);
            }

            var configuration = builder.Build();
            configuration.TopologiesPath = topologiesDirectory ?? configuration.TopologiesPath;

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).WithWebUi(port).Build();
        }

        private sealed class ListenerHold : IAsyncDisposable
        {
            private readonly TcpListener _listener;

            public ListenerHold(TcpListener listener)
            {
                _listener = listener;
            }

            public ValueTask DisposeAsync()
            {
                _listener.Stop();
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>
        ///     A console capture that survives being read while it is being written. The host under test
        ///     writes its receipts from its own threads while the waits above poll the buffer, and
        ///     <see cref="StringWriter" /> hands both straight to one unsynchronized
        ///     <see cref="StringBuilder" /> — a read that lands mid-append throws out of
        ///     <c>StringBuilder.ToString()</c>.
        /// </summary>
        private sealed class ConsoleCapture : TextWriter
        {
            private readonly StringBuilder _text = new();

            public override Encoding Encoding
            {
                get => Encoding.UTF8;
            }

            public override void Write(char value)
            {
                lock (_text)
                {
                    _text.Append(value);
                }
            }

            public override void Write(string? value)
            {
                lock (_text)
                {
                    _text.Append(value);
                }
            }

            public override void WriteLine(string? value)
            {
                lock (_text)
                {
                    _text.AppendLine(value);
                }
            }

            public override string ToString()
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }
    }
}