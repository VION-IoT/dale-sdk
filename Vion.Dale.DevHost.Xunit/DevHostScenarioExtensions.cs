using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Xunit
{
    /// <summary>
    ///     Thin run/assert helpers that pair an <see cref="IDevHost" /> with <see cref="ScenarioRunner" /> for
    ///     xUnit tests — so a test body is a one-liner instead of re-deriving the runner call + a status
    ///     assertion + the validation-error message. Deliberately named <c>…ScenarioAsync</c> so they never
    ///     collide with <see cref="IDevHost.RunAsync" /> (which runs the host until cancellation).
    /// </summary>
    public static class DevHostScenarioExtensions
    {
        /// <summary>
        ///     Run a scenario by id from a scenarios directory and return its structured report (failures are recorded, not
        ///     thrown).
        /// </summary>
        public static Task<ScenarioRunReport> RunScenarioAsync(this IDevHost host,
                                                               string id,
                                                               string? scenariosDir = null,
                                                               ScenarioRunOptions? options = null,
                                                               CancellationToken cancellationToken = default)
        {
            return ScenarioRunner.RunAsync(id, host.Control, scenariosDir, options, cancellationToken);
        }

        /// <summary>Run an already-parsed scenario and return its report.</summary>
        public static Task<ScenarioRunReport> RunScenarioAsync(this IDevHost host,
                                                               ScenarioFile scenario,
                                                               ScenarioRunOptions? options = null,
                                                               CancellationToken cancellationToken = default)
        {
            return ScenarioRunner.RunAsync(scenario, host.Control, options, cancellationToken);
        }

        /// <summary>
        ///     Apply a scenario by id as the arrange/stimulate phase of a test (RFC 0006 "Composition rule"):
        ///     setup + steps run, throwing <see cref="ScenarioRunException" /> on any failure, after which the
        ///     test adds its own typed assertions on <c>host.Control</c>.
        /// </summary>
        public static Task ApplyScenarioAsync(this IDevHost host,
                                              string id,
                                              string? scenariosDir = null,
                                              ScenarioRunOptions? options = null,
                                              CancellationToken cancellationToken = default)
        {
            return ScenarioRunner.ApplyAsync(id, host.Control, scenariosDir, options, cancellationToken);
        }

        /// <summary>
        ///     Assert the run succeeded, throwing <see cref="ScenarioRunException" /> (carrying the report) with
        ///     the first failed step's detail or the joined validation errors otherwise. Returns the report so it
        ///     can be chained: <c>(await host.RunScenarioAsync(id)).AssertSucceeded()</c>.
        /// </summary>
        public static ScenarioRunReport AssertSucceeded(this ScenarioRunReport report)
        {
            if (report.Status == ScenarioRunStatus.Succeeded)
            {
                return report;
            }

            var failure = report.Setup.Concat(report.Steps).FirstOrDefault(s => s.Status == ScenarioStepStatus.Failed);
            var detail = failure is null ? string.Join("; ", report.ValidationErrors) : $"step {failure.Index} ({failure.Label ?? failure.Target}): {failure.Detail}";
            throw new ScenarioRunException(report, $"Scenario '{report.ScenarioId}' did not succeed ({report.Status}): {detail}");
        }
    }
}