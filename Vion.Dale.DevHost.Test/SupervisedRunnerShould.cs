using System;
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
        public async Task PrintTheSameReadinessLineFromEitherOverload()
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
                await WaitForLineAsync(captured, "\"ready\"");
                await shutdown.CancelAsync();
                await runner;
            }
            finally
            {
                Console.SetOut(originalOut);
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
            }

            // Assert — a parser written against the supervised loop's line reads this one.
            var readiness = JsonDocument.Parse(ReceiptLine(captured, "\"ready\"")).RootElement;
            Assert.IsTrue(readiness.GetProperty("ready").GetBoolean());
            Assert.AreEqual(port, readiness.GetProperty("port").GetInt32());
            Assert.AreEqual(1, readiness.GetProperty("generation").GetInt32());
        }

        [TestMethod]
        public async Task StayOnTheRunningTopologyWhenTheNextOneCannotEvenBeBuilt()
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
                await WaitForLineAsync(captured, "\"ready\"");
                running!.Control.TryRequestTopologySwitch("gone");
                await WaitForLineAsync(captured, "cannot start");
                await WaitForLineAsync(captured, "\"generation\":3");
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
            Assert.AreEqual(3, JsonDocument.Parse(ReceiptLine(captured, "\"ready\"")).RootElement.GetProperty("generation").GetInt32());
        }

        [TestMethod]
        public async Task PrintAFailureReceiptWhenTheFirstGenerationCannotBeBuilt()
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
            var receipt = JsonDocument.Parse(ReceiptLine(captured, "\"failed\"")).RootElement;
            Assert.IsTrue(receipt.GetProperty("failed").GetBoolean());
            Assert.AreEqual(1, receipt.GetProperty("generation").GetInt32());
            StringAssert.Contains(receipt.GetProperty("reason").GetString()!, "no topology 'default'");
        }

        [TestMethod]
        public async Task RefuseToBindAPortAlreadyServedNamingIt()
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
        public async Task RefuseAnExportTargetItCannotWrite()
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

        private static string ReceiptLine(StringWriter captured, string token)
        {
            return captured.ToString().Split('\n').Select(l => l.Trim()).Last(l => l.Contains(token, StringComparison.Ordinal) && l.StartsWith('{'));
        }

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
