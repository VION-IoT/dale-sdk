using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;
using Xunit;

namespace Vion.Examples.Http.IntegrationTest
{
    /// <summary>
    ///     Runs the example's committed scenarios headlessly: the same files the DevHost Player runs, through the same
    ///     interpreter, with no web UI. The blocks are the real HTTP client and the real HTTP server talking over
    ///     <c>127.0.0.1:18080</c>, so a run exercises a real socket, a real refused connection and the platform's own
    ///     handler.
    ///     <para>
    ///         This is the tier the TestKit unit tests cannot reach. The client harness replaces the innermost message
    ///         handler and the server harness has no socket, so they prove what each block does with an answer, but never
    ///         that the two blocks understand each other on the wire.
    ///     </para>
    ///     <para>
    ///         <b>Real clock on purpose.</b> The host is built without <c>WithDeterministicStepping</c>: the server holds a
    ///         socket the host cannot step, and the client's latency would read zero on a clock nothing advances. The waits
    ///         here are therefore real waits.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     One class, so xunit runs the two scenarios in one collection — sequentially. Two collections would boot two hosts at
    ///     once and the second simulator could not bind port 18080.
    /// </remarks>
    [Trait("Category", "Smoke")]
    [Trait("kind", "headless-integration")]
    public class HttpRoundTripShould
    {
        /// <summary>
        ///     Boots a fresh host on the committed <c>default</c> topology and runs one committed scenario to completion. A
        ///     fresh host per scenario is required, not tidiness: both scenarios assert absolute request and hit counts, which
        ///     only hold on a simulator that has answered nothing before.
        /// </summary>
        private static async Task RunScenarioAsync(string id)
        {
            var exampleRoot = FindExampleRoot();
            var configuration = DevTopologyLoader.Load("default", Path.Combine(exampleRoot, "topologies"));

            await using var host = DevHostBuilder.Create().WithDi<DependencyInjection>().WithConfiguration(configuration).Build();
            await host.StartAsync(TestContext.Current.CancellationToken);

            var report = await ScenarioRunner.RunAsync(id, host.Control, Path.Combine(exampleRoot, "scenarios"), cancellationToken: TestContext.Current.CancellationToken);

            // The failing step's own detail is the diagnosis — "expected StatusCode equals 202, but was 200" — so it is the
            // assertion message, whole. Assert.Empty would print the collection cut to its first fifty characters, which
            // ends before the detail starts.
            var failures = report.ValidationErrors.Concat(report.Setup
                                                                .Concat(report.Steps)
                                                                .Where(step => step.Status == ScenarioStepStatus.Failed)
                                                                .Select(step => $"{step.Label ?? step.Target}: {step.Detail}"))
                                 .ToList();

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.Equal(ScenarioRunStatus.Succeeded, report.Status);
        }

        /// <summary>
        ///     Walks up from the test binaries to the example root — the folder holding <c>topologies/</c> and
        ///     <c>scenarios/</c>. Located rather than hard-coded so the same test works from <c>dotnet test</c>, an IDE runner
        ///     and CI, whose working directories all differ.
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
        public async Task ExchangeRequestAndAnswerOverSocket()
        {
            // Arrange

            // Act / Assert
            await RunScenarioAsync("http-roundtrip");
        }

        [Fact]
        public async Task TellEachFailureApartAndRecover()
        {
            // Arrange

            // Act / Assert
            await RunScenarioAsync("http-failures");
        }
    }
}
