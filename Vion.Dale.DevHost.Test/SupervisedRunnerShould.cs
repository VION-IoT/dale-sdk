using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        public async Task PrintSameReadinessLineFromEitherEntryPoint()
        {
            // Arrange
            var port = FreePort();
            await using var host = BuildWebHost(port);
            var originalOut = Console.Out;
            var captured = new StringWriter();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act — the unsupervised overload, the one whose line used to carry no generation at all.
            try
            {
                var runner = DevHostWebRunner.RunAsync(host, port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — a parser written against the supervised loop's line reads this one.
            var readiness = JsonDocument.Parse(ReceiptLine(captured, "\"ready\"", port)).RootElement;
            Assert.IsTrue(readiness.GetProperty("ready").GetBoolean());
            Assert.AreEqual(port, readiness.GetProperty("port").GetInt32());
            Assert.AreEqual(1, readiness.GetProperty("generation").GetInt32());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-005.5")]
        public async Task StayOnRunningTopologyWhenNextOneCannotBeBuilt()
        {
            // Arrange — the factory throws for the switched-to id, the way a deleted topology file does.
            var port = FreePort();
            var originalOut = Console.Out;
            var captured = new StringWriter();
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
            try
            {
                var runner = DevHostWebRunner.RunAsync(Factory, port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                running!.Control.TryRequestTopologySwitch("gone");
                await WaitForLineAsync(captured, "cannot start");
                await WaitForReceiptAsync(captured, "\"generation\":3", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
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
            var captured = new StringWriter();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            try
            {
                await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
                                                                           DevHostWebRunner.RunAsync(_ => throw new FileNotFoundException("no topology 'default'"),
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
        [TestProperty("spec", "AC-CTRL-002.6")]
        public async Task RefuseToBindPortAlreadyServedNamingIt()
        {
            // Arrange — the commonest first-run failure: a second `dale dev` in one checkout. Kestrel reports
            // it as an IOException, which the supervisor's own catch does not see.
            var port = FreePort();
            await using var first = BuildWebHost(port);
            await first.StartAsync();
            await using var second = BuildWebHost(port);

            // Act
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => second.StartAsync());

            // Assert
            StringAssert.Contains(refusal.Message, $"bind port {port}");
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
            var captured = new StringWriter();
            using var shutdown = new CancellationTokenSource();
            IDevHost? running = null;
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act — Ctrl+C during a generation must stop that host and return, not build another.
            try
            {
                var runner = DevHostWebRunner.RunAsync(_ => running = BuildWebHost(port), port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
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
        public async Task PrintAddressAndScenarioDeepLinksBeforeReadiness()
        {
            // Arrange — one scenario that parses and one that does not; the operator needs both named.
            var directory = Path.Combine(Path.GetTempPath(), "dale-links-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "good.scenario.json"),
                              """{ "version": 1, "id": "good", "title": "Good", "topology": "counter-topology", "steps": [] }""");
            File.WriteAllText(Path.Combine(directory, "broken.scenario.json"), """{ "version": 7 }""");
            var port = FreePort();
            var configuration = DevConfigurationBuilder.Create()
                                                       .WithTopologyName("counter-topology")
                                                       .WithScenarios(directory)
                                                       .AddLogicBlock<CounterBlock>("counter")
                                                       .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).WithWebUi(port).Build();
            var originalOut = Console.Out;
            var captured = new StringWriter();
            using var shutdown = new CancellationTokenSource();
            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            Console.SetOut(captured);

            // Act
            try
            {
                var runner = DevHostWebRunner.RunAsync(host, port, shutdown.Token);
                await WaitForReceiptAsync(captured, "\"ready\"", port);
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — both links, and both before the readiness line an agent waits for.
            var console = captured.ToString();
            StringAssert.Contains(console, $"http://localhost:{port}");
            StringAssert.Contains(console, $"scenario good: http://localhost:{port}/#/scenario/good");
            StringAssert.Contains(console, "scenario broken: INVALID");
            Assert.IsLessThan(console.IndexOf("\"ready\"", StringComparison.Ordinal),
                              console.IndexOf("scenario good", StringComparison.Ordinal),
                              "the deep links come before the readiness line, so an agent can read them and then wait");
        }

        // Console.SetOut is process-wide, so a host another suite is still tearing down writes into this
        // test's capture too — and its readiness receipt has the same shape as this one's. Every receipt
        // carries the port it belongs to, so that is what the lines are filtered on rather than the token
        // alone. Without it this suite passes alone and fails intermittently in the full run.
        private static string ReceiptLine(StringWriter captured, string token, int port)
        {
            return ReceiptLines(captured, token, port).Last();
        }

        private static IEnumerable<string> ReceiptLines(StringWriter captured, string token, int port)
        {
            return captured.ToString()
                           .Split('\n')
                           .Select(l => l.Trim())
                           .Where(l => l.StartsWith('{') && l.Contains(token, StringComparison.Ordinal) && l.Contains($"\"port\":{port}", StringComparison.Ordinal));
        }

        private static async Task WaitForReceiptAsync(StringWriter captured, string token, int port)
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
        private static async Task WaitForLineAsync(StringWriter captured, string token)
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

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static IDevHost BuildWebHost(int port)
        {
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("counter-topology").AddLogicBlock<CounterBlock>("counter").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).WithWebUi(port).Build();
        }
    }
}
