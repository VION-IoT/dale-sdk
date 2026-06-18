using System;
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
    ///     tools/agents, backed entirely by the one control abstraction <see cref="IDevHostControl" /> (RFC 0003).
    /// </summary>
    [ApiController]
    [Route("api")]
    public class DevHostController : ControllerBase
    {
        private readonly IDevHostControl _control;

        private readonly ScenarioRunRegistry _runs;

        public DevHostController(IDevHostControl control, ScenarioRunRegistry runs)
        {
            _control = control;
            _runs = runs;
        }

        // --- Configuration & writes ---

        /// <summary>Full introspection — services, property schemas, presentation, wiring — for rendering.</summary>
        [HttpGet("configuration")]
        public ActionResult<ConfigurationOutput> GetConfiguration()
        {
            return Ok(_control.GetConfiguration());
        }

        [HttpPost("hal/di/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetDigitalInputValue(string serviceProviderIdentifier,
                                                             string serviceIdentifier,
                                                             string contractIdentifier,
                                                             [FromBody] SetValueInput<bool> input)
        {
            await _control.SetDigitalInputAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        [HttpPost("hal/ai/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetAnalogInputValue(string serviceProviderIdentifier,
                                                            string serviceIdentifier,
                                                            string contractIdentifier,
                                                            [FromBody] SetValueInput<double> input)
        {
            await _control.SetAnalogInputAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        /// <summary>
        ///     The last value a block Set on a mocked digital output (the read complement of the
        ///     <c>hal/di</c> POST) — <c>{ value }</c> is the cached bool, or null if the output was never Set.
        /// </summary>
        [HttpGet("hal/do/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public ActionResult GetDigitalOutputValue(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier)
        {
            return Ok(new { value = _control.GetDigitalOutput(serviceProviderIdentifier, serviceIdentifier, contractIdentifier) });
        }

        /// <summary>
        ///     The last value a block Set on a mocked analog output (the read complement of the <c>hal/ai</c>
        ///     POST) — <c>{ value }</c> is the cached double, or null if the output was never Set.
        /// </summary>
        [HttpGet("hal/ao/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public ActionResult GetAnalogOutputValue(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier)
        {
            return Ok(new { value = _control.GetAnalogOutput(serviceProviderIdentifier, serviceIdentifier, contractIdentifier) });
        }

        [HttpPost("dale/property/{serviceIdentifier}/{propertyIdentifier}")]
        public async Task<ActionResult> SetServicePropertyValue(string serviceIdentifier, string propertyIdentifier, [FromBody] SetValueInput<object> input)
        {
            try
            {
                await _control.SetServicePropertyValueAsync(serviceIdentifier, propertyIdentifier, input.Value);
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                // Unknown service id, or a read-only / unknown member — a write the block can't apply. Fail
                // loudly with a 4xx instead of a silently-timed-out 200.
                return BadRequest(new { error = ex.Message });
            }
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

        // --- Run control (pause / resume / reset) ---

        /// <summary>Run-control state: paused? supervisor attached (reset possible)? stepped? + the virtual clock.</summary>
        [HttpGet("control/status")]
        public ActionResult GetControlStatus()
        {
            return Ok(new
                      {
                          paused = _control.IsPaused,
                          canReset = _control.CanReset,
                          stepped = _control.IsStepped,
                          virtualTimeUtc = _control.VirtualTimeUtc,
                      });
        }

        /// <summary>
        ///     Manual stepping (RFC 0008 §Part 4): advance the virtual clock to the next scheduled event and
        ///     quiesce — the atomic "step" of the deterministic why-loop. 409 unless the host is stepped and
        ///     no scenario run is driving the clock.
        /// </summary>
        [HttpPost("control/step")]
        public async Task<ActionResult> Step()
        {
            if (StepConflict() is { } conflict)
            {
                return conflict;
            }

            await _control.AdvanceToNextEventAsync();
            return Ok(new { virtualTimeUtc = _control.VirtualTimeUtc });
        }

        /// <summary>
        ///     Manual stepping (RFC 0008 §Part 4): advance the virtual clock by <paramref name="seconds" /> of
        ///     simulated time, firing every event due within it. Same 409 guards as <see cref="Step" />.
        /// </summary>
        [HttpPost("control/advance")]
        public async Task<ActionResult> Advance([FromQuery] double seconds)
        {
            if (seconds <= 0)
            {
                return BadRequest(new { error = "seconds must be a positive number" });
            }

            if (StepConflict() is { } conflict)
            {
                return conflict;
            }

            await _control.AdvanceAsync(TimeSpan.FromSeconds(seconds));
            return Ok(new { virtualTimeUtc = _control.VirtualTimeUtc });
        }

        /// <summary>
        ///     Pause time-driven activity (new timer ticks and delayed callbacks are held; message handling
        ///     continues — see <see cref="IDevHostControl.Pause" /> for the exact semantics).
        /// </summary>
        [HttpPost("control/pause")]
        public ActionResult Pause()
        {
            _control.Pause();
            return Ok(new { paused = true });
        }

        /// <summary>Resume: replay held timer ticks / delayed callbacks with their original delays.</summary>
        [HttpPost("control/resume")]
        public ActionResult Resume()
        {
            _control.Resume();
            return Ok(new { paused = false });
        }

        /// <summary>
        ///     Recycle the host (dispose → rebuild → restart). 202 when a supervisor picked it up; 409 when
        ///     the host runs unsupervised (started with a built host instead of
        ///     <c>DevHostWebRunner.RunAsync(hostFactory, …)</c>).
        /// </summary>
        [HttpPost("control/reset")]
        public ActionResult Reset()
        {
            if (!_control.TryRequestReset())
            {
                return Conflict(new { error = "Host is not supervised — pass a host factory to DevHostWebRunner.RunAsync to enable reset." });
            }

            return Accepted();
        }

        /// <summary>Inter-block messages captured by the tap, optionally filtered to a logic block (by name or id).</summary>
        [HttpGet("messages")]
        public ActionResult GetMessages([FromQuery] string? logicBlock = null)
        {
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

        // The shared guard for manual stepping: only meaningful on a stepped host, and never while a scenario
        // run is driving the clock (the two would race on the shared virtual schedule).
        private ActionResult? StepConflict()
        {
            if (!_control.IsStepped)
            {
                return Conflict(new { error = "not a stepped host — start it with `dale dev --stepped` to step the virtual clock by hand" });
            }

            if (_runs.HasActiveRun)
            {
                return Conflict(new { error = "a scenario run is driving the clock — stepping is unavailable until it finishes" });
            }

            return null;
        }
    }
}