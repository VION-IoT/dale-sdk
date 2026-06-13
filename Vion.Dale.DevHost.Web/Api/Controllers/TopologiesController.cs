using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     Topology discovery and switching (RFC 0006 R5). Switching rides the R2 run-control reset: the
    ///     request parks the topology id on the run control and triggers a recycle; a topology-aware
    ///     supervisor (<c>DevHostWebRunner.RunAsync(Func&lt;string?, IDevHost&gt;, …)</c>) builds the next
    ///     generation from it. Without such a supervisor the switch is refused, like reset.
    /// </summary>
    [ApiController]
    [Route("api/topologies")]
    public class TopologiesController : ControllerBase
    {
        private readonly IDevHostControl _control;

        private readonly DevTopologyStore _store;

        public TopologiesController(DevTopologyStore store, IDevHostControl control)
        {
            _store = store;
            _control = control;
        }

        /// <summary>The generic topology-file JSON Schema shipped with the DevHost (RFC 0006 R5).</summary>
        [HttpGet("schema")]
        public IActionResult Schema()
        {
            var assembly = typeof(DevTopologyFile).Assembly;
            using var stream = assembly.GetManifestResourceStream("Vion.Dale.DevHost.Topologies.topology.schema.json");
            if (stream is null)
            {
                return NotFound(new { error = "embedded topology schema missing from the DevHost assembly" });
            }

            using var reader = new StreamReader(stream);
            return Content(reader.ReadToEnd(), "application/json");
        }

        /// <summary>Discovered topology files plus the currently running topology and switchability.</summary>
        [HttpGet]
        public IActionResult List()
        {
            return Ok(new
                      {
                          current = _control.GetConfiguration().TopologyName,
                          canSwitch = _control.CanReset,
                          directory = _store.Directory,
                          topologies = _store.List(),
                      });
        }

        /// <summary>
        ///     Recycle the host into this topology. 202 when the supervisor accepted; 409 when the host
        ///     runs without a topology-aware supervisor; 404 for unknown ids.
        /// </summary>
        [HttpPost("{id}/switch")]
        public IActionResult Switch(string id)
        {
            if (_store.List().All(t => t.Id != id))
            {
                return NotFound(new { error = $"no topology '{id}' under {_store.Directory}" });
            }

            if (!_control.TryRequestTopologySwitch(id))
            {
                return Conflict(new
                                {
                                    error = "host is not supervised — topology switching needs DevHostWebRunner.RunAsync with a topology-aware host factory",
                                });
            }

            return Accepted(new { switching = id });
        }
    }
}