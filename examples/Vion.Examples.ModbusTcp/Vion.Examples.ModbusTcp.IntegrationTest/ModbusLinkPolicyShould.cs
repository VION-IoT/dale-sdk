using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;
using Xunit;

namespace Vion.Examples.ModbusTcp.IntegrationTest
{
    /// <summary>
    ///     Runs the example's committed scenarios headlessly (RFC 0003 + RFC 0006): the same files the DevHost
    ///     Player and <c>scripts/smoke-modbus.ps1</c> run, through the same interpreter, with no web UI. The
    ///     blocks are a real Modbus TCP client and server talking over <c>127.0.0.1:15020</c>, so a run
    ///     exercises real sockets, a real refused connect and the real backoff timer.
    ///     <para>
    ///         This is the tier the TestKit unit tests cannot reach. The fake client proxy answers every
    ///         request from a register store, so it can prove the block decodes and recovers, but never that
    ///         a socket faults, that the second consecutive failed connect arms a backoff, or that a changed
    ///         port ends one.
    ///     </para>
    ///     <para>
    ///         <b>Real clock on purpose.</b> The host is built without <c>WithDeterministicStepping</c>: the
    ///         TCP client's sockets and timeouts are real time, so under a virtual clock a backoff would never
    ///         elapse and every round trip would read zero. The waits here are therefore real waits, which is
    ///         why the link-policy run takes about half a minute.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     One class, so xunit runs the two scenarios in one collection — sequentially. Two collections would
    ///     boot two hosts at once and the second sim server could not bind port 15020.
    /// </remarks>
    [Trait("Category", "Smoke")]
    [Trait("kind", "headless-integration")]
    public class ModbusLinkPolicyShould
    {
        /// <summary>
        ///     Boots a fresh host on the committed <c>default</c> topology and runs one committed scenario to
        ///     completion. A fresh host per scenario is required, not tidiness: the link-policy scenario asserts
        ///     absolute connect counts, which only hold on a client that has not connected before.
        /// </summary>
        private static async Task RunScenarioAsync(string id)
        {
            var exampleRoot = FindExampleRoot();
            var configuration = DevTopologyLoader.Load("default", Path.Combine(exampleRoot, "topologies"));

            await using var host = DevHostBuilder.Create().WithDi<DependencyInjection>().WithConfiguration(configuration).Build();
            await host.StartAsync(TestContext.Current.CancellationToken);

            var report = await ScenarioRunner.RunAsync(id, host.Control, Path.Combine(exampleRoot, "scenarios"), cancellationToken: TestContext.Current.CancellationToken);

            // The failing step's own detail is the diagnosis — "expected Link above 1, but was 1" — so put it
            // in the assertion message rather than making the reader re-run the scenario to find out.
            var failures = report.ValidationErrors.Concat(report.Setup
                                                                .Concat(report.Steps)
                                                                .Where(step => step.Status == ScenarioStepStatus.Failed)
                                                                .Select(step => $"{step.Label ?? step.Target}: {step.Detail}"));

            Assert.Equal(ScenarioRunStatus.Succeeded, report.Status);
            Assert.Empty(failures);
        }

        /// <summary>
        ///     Walks up from the test binaries to the example root — the folder holding <c>topologies/</c> and
        ///     <c>scenarios/</c>. Located rather than hard-coded so the same test works from
        ///     <c>dotnet test</c>, an IDE runner and CI, whose working directories all differ.
        /// </summary>
        private static string FindExampleRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "scenarios")) && Directory.Exists(Path.Combine(directory.FullName, "topologies")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException($"No example root with topologies/ and scenarios/ above {AppContext.BaseDirectory}.");
        }

        [Fact]
        public async Task FaultBackOffRecoverAndStayOnlineUnderLoad()
        {
            await RunScenarioAsync("modbus-link-policy");
        }

        [Fact]
        public async Task ReadTheSimulatorAndReportAHealthyLink()
        {
            await RunScenarioAsync("modbus-healthy");
        }
    }
}