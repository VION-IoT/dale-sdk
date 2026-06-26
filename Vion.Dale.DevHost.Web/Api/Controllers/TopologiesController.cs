using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
                          readOnly = DevTopologyStore.IsReadOnly,
                          directory = _store.Directory,
                          topologies = _store.List(),
                      });
        }

        /// <summary>The topology file, byte-for-byte as on disk.</summary>
        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var raw = _store.ReadRaw(id);
            return raw is null ? NotFound(new { error = $"no topology '{id}' under {_store.Directory}" }) : Content(raw, "application/json");
        }

        /// <summary>
        ///     Save a topology from the editor (RFC 0013): structurally + catalog + compatibility validated,
        ///     path-confined to the topologies directory, disabled by <c>DALE_DEVHOST_READONLY_TOPOLOGIES=1</c>.
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
            catch (InvalidDataException e)
            {
                return UnprocessableEntity(new { valid = false, errors = e.Message.Split("; ") });
            }
        }

        /// <summary>
        ///     Validate a draft topology (RFC 0013) without writing it: structural + catalog + compatibility,
        ///     in-process against the live catalog. The draft may be un-named (a brand-new draft has no saved id).
        /// </summary>
        [HttpPost("validate")]
        public async Task<IActionResult> Validate()
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();

            try
            {
                DevTopologyLoader.Build(DevTopologyFile.Parse(raw));
                return Ok(new { valid = true });
            }
            catch (InvalidDataException e)
            {
                return UnprocessableEntity(new { valid = false, errors = e.Message.Split("; ") });
            }
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