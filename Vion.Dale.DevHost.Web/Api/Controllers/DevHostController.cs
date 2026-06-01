using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Web.Api.Dtos;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     The DevHost HTTP API under <c>/api</c> — the single surface shared by the web UI and headless
    ///     tools/agents. Write/configuration endpoints are backed by <see cref="IDevHostStateProvider" /> (the
    ///     UI's existing contract); the read/observe endpoints are backed by the headless
    ///     <see cref="IDevHostControl" /> (RFC 0003). One controller so both clients use one documented API.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class DevHostController : ControllerBase
    {
        private readonly IDevHostStateProvider _stateProvider;

        private readonly IDevHostControl _control;

        public DevHostController(IDevHostStateProvider stateProvider, IDevHostControl control)
        {
            _stateProvider = stateProvider;
            _control = control;
        }

        // --- Configuration & writes (UI's existing contract) ---

        /// <summary>Full introspection — services, property schemas, presentation, wiring — for rendering.</summary>
        [HttpGet("configuration")]
        public async Task<ActionResult<ConfigurationOutput>> GetConfiguration()
        {
            var config = await _stateProvider.GetConfigurationAsync();
            return Ok(config);
        }

        [HttpPost("hal/di/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetDigitalInputValue(string serviceProviderIdentifier,
                                                             string serviceIdentifier,
                                                             string contractIdentifier,
                                                             [FromBody] SetValueInput<bool> input)
        {
            await _stateProvider.SetDigitalInputValueAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        [HttpPost("hal/ai/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetAnalogInputValue(string serviceProviderIdentifier,
                                                            string serviceIdentifier,
                                                            string contractIdentifier,
                                                            [FromBody] SetValueInput<double> input)
        {
            await _stateProvider.SetAnalogInputValueAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        [HttpPost("dale/property/{serviceIdentifier}/{propertyIdentifier}")]
        public async Task<ActionResult> SetServicePropertyValue(string serviceIdentifier, string propertyIdentifier, [FromBody] SetValueInput<object> input)
        {
            await _stateProvider.SetServicePropertyValueAsync(serviceIdentifier, propertyIdentifier, input.Value);
            return Ok();
        }

        // --- Read / observe (headless control surface, RFC 0003) ---

        /// <summary>Lightweight topology (id / name / type / service ids) — the scripting-friendly view of <c>/configuration</c>.</summary>
        [HttpGet("logicblocks")]
        public ActionResult GetLogicBlocks()
        {
            return Ok(_control.ListLogicBlocks());
        }

        /// <summary>All last-known service-property and measuring-point values for a logic block (by name or id).</summary>
        [HttpGet("state/{logicBlockIdOrName}")]
        public ActionResult GetState(string logicBlockIdOrName)
        {
            return Ok(_control.GetAllProperties(logicBlockIdOrName));
        }

        /// <summary>The last-known value of a single service property or measuring point.</summary>
        [HttpGet("state/{logicBlockIdOrName}/{propertyName}")]
        public ActionResult GetState(string logicBlockIdOrName, string propertyName)
        {
            return Ok(new { logicBlockIdOrName, propertyName, value = _control.GetProperty(logicBlockIdOrName, propertyName) });
        }

        /// <summary>Recent captured log lines (bounded scrollback) — the machine-readable console.</summary>
        [HttpGet("logs/recent")]
        public ActionResult GetRecentLogs([FromQuery] int max = 500)
        {
            var lines = _control.RecentLogs(max)
                                .Select(l => new
                                             {
                                                 level = l.Level.ToString(),
                                                 category = l.Category,
                                                 timestamp = l.Timestamp,
                                                 message = l.Message,
                                                 exception = l.Exception,
                                             });
            return Ok(lines);
        }

        /// <summary>Inter-block messages captured by the tap, optionally filtered to a logic block (by name or id).</summary>
        [HttpGet("messages")]
        public ActionResult GetMessages([FromQuery] string? logicBlock = null)
        {
            // The raw message instance can be an arbitrary CLR type; project to a serialization-safe shape.
            var messages = _control.RecordedMessages(logicBlock)
                                   .Select(m => new
                                                {
                                                    actor = m.ActorName,
                                                    type = m.MessageType,
                                                    message = m.Message.ToString(),
                                                    timestamp = m.Timestamp,
                                                });
            return Ok(messages);
        }
    }
}
