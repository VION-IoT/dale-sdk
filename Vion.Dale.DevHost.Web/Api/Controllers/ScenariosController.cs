using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     The scenario surface (RFC 0006, additive to RFC 0003's <c>/api</c>): discovery, file serving,
    ///     run triggering, run status, and the Explorer's save-as-scenario. Scenario files are served and
    ///     stored byte-for-byte — the parsed model exists for validation and the runner, not as a wire shape.
    /// </summary>
    [ApiController]
    [Route("api/scenarios")]
    public class ScenariosController : ControllerBase
    {
        private readonly IDevHostControl _control;

        private readonly ScenarioRunRegistry _registry;

        private readonly ScenarioStore _store;

        public ScenariosController(ScenarioStore store, ScenarioRunRegistry registry, IDevHostControl control)
        {
            _store = store;
            _registry = registry;
            _control = control;
        }

        /// <summary>Discovered scenarios (broken files included, carrying their parse error) + store facts.</summary>
        [HttpGet]
        public IActionResult List()
        {
            return Ok(new
                      {
                          directory = _store.Directory,
                          readOnly = ScenarioStore.IsReadOnly,
                          scenarios = _store.List(),
                      });
        }

        /// <summary>The generic scenario-file JSON Schema shipped with the DevHost (RFC 0006).</summary>
        [HttpGet("schema")]
        public IActionResult Schema()
        {
            var assembly = typeof(ScenarioRunner).Assembly;
            using var stream = assembly.GetManifestResourceStream("Vion.Dale.DevHost.Scenarios.scenario.schema.json");
            if (stream is null)
            {
                return NotFound(new { error = "embedded scenario schema missing from the DevHost assembly" });
            }

            using var reader = new StreamReader(stream);
            return Content(reader.ReadToEnd(), "application/json");
        }

        /// <summary>The scenario file, byte-for-byte as on disk.</summary>
        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var raw = _store.ReadRaw(id);
            if (raw is null)
            {
                return NotFound(new { error = $"no scenario '{id}'" });
            }

            return Content(raw, "application/json");
        }

        /// <summary>The latest run's status for this scenario (404 when it never ran this host generation).</summary>
        [HttpGet("{id}/run")]
        public IActionResult LatestRun(string id)
        {
            var report = _registry.Latest(id);
            if (report is null)
            {
                return NotFound(new { error = $"scenario '{id}' has not run yet" });
            }

            return Ok(report);
        }

        /// <summary>
        ///     Start a run (RFC 0008 recycle-on-run). A scenario runs against the topology it declares, from a
        ///     clean slate — so every run is reproducible. When the host is on a different topology, or this
        ///     stepped generation has already been advanced/run (dirty), the host is recycled onto the
        ///     scenario's topology first (a fresh generation: epoch clock, freshly-instantiated blocks) and the
        ///     response is <c>{ recycling: true }</c> — the caller re-applies once the host is back. A clean,
        ///     matching host runs in place. One active run per host: 409 while another is active unless
        ///     <c>?restart=true</c> (cancels it first). There is no <c>force</c>: running against the wrong
        ///     topology or a dirty clock silently produced misleading results.
        /// </summary>
        [HttpPost("{id}/apply")]
        public async Task<IActionResult> Apply(string id, [FromQuery] bool restart = false)
        {
            var raw = _store.ReadRaw(id);
            if (raw is null)
            {
                return NotFound(new { error = $"no scenario '{id}'" });
            }

            ScenarioFile scenario;
            try
            {
                scenario = ScenarioFile.Parse(raw);
            }
            catch (ScenarioFormatException e)
            {
                return UnprocessableEntity(new { error = "scenario file is not structurally valid", errors = e.Errors });
            }

            if (scenario.Id != id)
            {
                return UnprocessableEntity(new { error = $"id '{scenario.Id}' does not match the file name '{id}'" });
            }

            // Recycle-on-run: bring the host to the scenario's topology + a clean slate before running, so the
            // result is reproducible. "Dirty" (needs a clean slate) is a stepped generation whose clock has
            // advanced or that has already run a scenario — the same generation re-run would otherwise build on
            // leftover state. A topology mismatch always needs a recycle (you cannot run a scenario against the
            // wrong graph).
            var hostTopology = _control.GetConfiguration().TopologyName;
            var topologyMatches = string.Equals(scenario.Topology, hostTopology, StringComparison.Ordinal);
            var dirty = _control.HasAdvancedFromBaseline || (_control.IsStepped && _registry.Latest(id) is not null);

            if (!topologyMatches || dirty)
            {
                if (_control.CanReset)
                {
                    // Rides the topology-switch recycle: the supervisor rebuilds the host onto this topology
                    // with a fresh clock and blocks. The caller polls until the host is back, then re-applies
                    // against the now-clean, matching generation (which runs in place).
                    _control.TryRequestTopologySwitch(scenario.Topology!);
                    return Accepted(new { recycling = true, topology = scenario.Topology });
                }

                if (!topologyMatches)
                {
                    // No supervisor to recycle and the topology is wrong — a setup error the caller must fix.
                    return Conflict(new
                                    {
                                        error =
                                            $"host is on topology '{hostTopology}', scenario '{id}' expects '{scenario.Topology}', and this host has no supervisor to recycle — build it on '{scenario.Topology}'.",
                                        scenarioTopology = scenario.Topology,
                                        hostTopology,
                                    });
                }

                // Unsupervised, right topology, but a dirty stepped clock: a clean slate isn't possible without
                // a supervisor, so run in place — the report's virtual start time reflects the non-clean clock.
            }

            var result = await _registry.ApplyAsync(scenario, _control, restart, _store.FileHash(id));
            if (result.IsConflict)
            {
                return Conflict(new
                                {
                                    reason = "runAlreadyActive",
                                    error = $"a run is already active (scenario '{result.ActiveScenarioId}') — pass ?restart=true to cancel it",
                                    activeRunId = result.RunId,
                                    activeScenarioId = result.ActiveScenarioId,
                                });
            }

            return Accepted(new { runId = result.RunId });
        }

        /// <summary>
        ///     Save a scenario from the Explorer (approved write-to-disk): structurally validated,
        ///     path-confined to the scenarios directory, disabled by <c>DALE_DEVHOST_READONLY_SCENARIOS=1</c>.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Save(string id)
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();

            try
            {
                var path = _store.Save(id, raw);
                return Ok(new { saved = Path.GetFileName(path), directory = _store.Directory });
            }
            catch (InvalidOperationException e)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = e.Message });
            }
            catch (ScenarioFormatException e)
            {
                return UnprocessableEntity(new { error = "scenario file is not structurally valid", errors = e.Errors });
            }
        }
    }
}