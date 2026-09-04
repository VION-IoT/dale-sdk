using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Web.Api.Dtos;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     The DevHost HTTP API under <c>/api</c> — the single surface shared by the web UI and headless
    ///     tools/agents, backed entirely by the one control abstraction <see cref="IDevHostControl" />.
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

        /// <summary>
        ///     Drive any service-provider value <em>input</em> contract — the one manual-drive endpoint
        ///     behind the web UI's HAL controls. The UI builds the wire value from the rendered control (a bool
        ///     for a toggle, a number for an analog field, an object for a struct contract) and posts it as
        ///     <c>{ value }</c>; <paramref name="handlerName" /> is the contract's stand-in actor name from the
        ///     configuration (its <c>contractHandlerActorName</c> annotation).
        /// </summary>
        [HttpPost("contracts/drive/{handlerName}/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> DriveContract(string handlerName,
                                                      string serviceProviderIdentifier,
                                                      string serviceIdentifier,
                                                      string contractIdentifier,
                                                      [FromBody] SetValueInput<JsonElement> input)
        {
            try
            {
                await _control.DriveServiceProviderContractAsync(handlerName, serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
                return Ok();
            }
            catch (ServiceProviderDriveException ex)
            {
                // A drive that would reach no block — fail loudly with the same structured shape the write
                // path uses, rather than the 200 that told the UI's HAL toggle the poke took.
                return BadRequest(new { error = ex.Message, reason = ex.Reason, contract = ex.Contract });
            }
        }

        [HttpPost("dale/property/{serviceIdentifier}/{propertyIdentifier}")]
        public async Task<ActionResult> SetServicePropertyValue(string serviceIdentifier, string propertyIdentifier, [FromBody] SetValueInput<object> input)
        {
            try
            {
                await _control.SetServicePropertyValueAsync(serviceIdentifier, propertyIdentifier, input.Value);
                return Ok();
            }
            catch (ServicePropertyWriteException ex)
            {
                // A write the block can't apply (unknown service / unknown member / read-only) — fail loudly
                // with a structured 4xx (reason + property) so tooling/agents can act without string-matching
                // the message, instead of a silently-timed-out 200.
                return BadRequest(new { error = ex.Message, reason = ex.Reason, property = ex.Property });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // --- Read / observe (the headless control surface) ---

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
            if (!KnowsLogicBlock(logicBlockIdOrName))
            {
                return NotFound(new { error = $"no logic block '{logicBlockIdOrName}' in the wired network", reason = "unknownLogicBlock" });
            }

            return Ok(_control.GetAllProperties(logicBlockIdOrName));
        }

        /// <summary>The last-known value of a single service property or measuring point.</summary>
        [HttpGet("state/{logicBlockIdOrName}/{propertyName}")]
        public ActionResult GetState(string logicBlockIdOrName, string propertyName)
        {
            if (!KnowsLogicBlock(logicBlockIdOrName))
            {
                return NotFound(new { error = $"no logic block '{logicBlockIdOrName}' in the wired network", reason = "unknownLogicBlock" });
            }

            if (!KnowsMember(logicBlockIdOrName, propertyName))
            {
                return NotFound(new { error = $"logic block '{logicBlockIdOrName}' carries no member '{propertyName}'", reason = "unknownMember" });
            }

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

        /// <summary>Run-control state: paused? supervisor attached (reset possible)? stepped? + the virtual clock and the block failures the host recorded.</summary>
        [HttpGet("control/status")]
        public ActionResult GetControlStatus()
        {
            return Ok(new
                      {
                          paused = _control.IsPaused,
                          canReset = _control.CanReset,
                          stepped = _control.IsStepped,
                          virtualTimeUtc = _control.VirtualTimeUtc,
                          runActive = _runs.HasActiveRun,

                          // Every handler exception a block threw and the middleware swallowed. Empty on a
                          // healthy host; non-empty is the one signal that the host started over a block that
                          // did not, whose members then read their defaults with no other trace.
                          blockFailures = _control.RecordedFailures()
                                                  .Select(f => new
                                                               {
                                                                   logicBlock = f.LogicBlock,
                                                                   message = f.MessageType,
                                                                   error = f.Error,
                                                                   timestamp = f.Timestamp,
                                                               }),
                      });
        }

        /// <summary>
        ///     Manual stepping (the stepped-host enabler): advance the virtual clock to the next scheduled event and
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
        ///     Manual stepping (the stepped-host enabler): advance the virtual clock by <paramref name="seconds" /> of
        ///     simulated time, firing every event due within it. Same 409 guards as <see cref="Step" />.
        /// </summary>
        [HttpPost("control/advance")]
        public async Task<ActionResult> Advance([FromQuery] double seconds)
        {
            // The same bound a scenario's durations carry (AC-SCEN-003.2) — a manual advance is the same clock.
            // `seconds <= 0` alone lets a non-number through: NaN fails every comparison, so it reached
            // TimeSpan.FromSeconds and escaped as a 500 rather than a refusal naming the bound.
            if (!(seconds > 0) || seconds > ScenarioFile.MaxDurationSeconds)
            {
                return BadRequest(new { error = $"seconds must be a number greater than 0 and at most {ScenarioFile.MaxDurationSeconds} (what a real clock can wait)", reason = "badDuration" });
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
                return Conflict(new { error = "Host is not supervised — pass a host factory to DevHostWebRunner.RunAsync to enable reset.", reason = "notSupervised" });
            }

            return Accepted();
        }

        /// <summary>
        ///     Switch the host's clock mode: rebuild the host stepped (deterministic) or real
        ///     (wall-clock). Rides the recycle — 202 when a supervisor picked it up, 409 when unsupervised.
        /// </summary>
        [HttpPost("control/clock-mode")]
        public ActionResult ClockMode([FromQuery] bool stepped)
        {
            if (!_control.TryRequestClockMode(stepped))
            {
                return Conflict(new { error = "Host is not supervised — clock-mode switching needs DevHostWebRunner.RunAsync with a host factory.", reason = "notSupervised" });
            }

            return Accepted(new { recycling = true, stepped });
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

        // Whether the wired network carries this block, by the same name-or-id addressing the read itself uses.
        // The in-process members keep their documented "null / empty for unknown" contract; the refusal lives
        // here, where a caller has a status code to act on.
        private bool KnowsLogicBlock(string logicBlockIdOrName)
        {
            return _control.ListLogicBlocks().Any(b => b.Name == logicBlockIdOrName || b.Id == logicBlockIdOrName);
        }

        // Whether the block carries this member, in either addressing form: a bare member name (the flat
        // per-block map the state route serves) or the dotted "service.member" path that reaches a nested
        // component's member. The dotted form is resolved against the configuration, because a shadowed
        // nested member is deliberately absent from the flat map's keys.
        private bool KnowsMember(string logicBlockIdOrName, string propertyName)
        {
            if (_control.GetAllProperties(logicBlockIdOrName).ContainsKey(propertyName))
            {
                return true;
            }

            var separatorIndex = propertyName.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= propertyName.Length - 1)
            {
                return false;
            }

            var serviceIdentifier = propertyName[..separatorIndex];
            var member = propertyName[(separatorIndex + 1)..];
            var logicBlock = _control.GetConfiguration().LogicBlocks.FirstOrDefault(b => b.Name == logicBlockIdOrName || b.Id == logicBlockIdOrName);
            var service = logicBlock?.Services.FirstOrDefault(s => s.Identifier == serviceIdentifier);

            return service is not null &&
                   (service.ServiceProperties.Any(sp => sp.Identifier == member) || service.ServiceMeasuringPoints.Any(mp => mp.Identifier == member));
        }

        // The shared guard for manual stepping: only meaningful on a stepped host, and never while a scenario
        // run is driving the clock (the two would race on the shared virtual schedule).
        private ActionResult? StepConflict()
        {
            if (!_control.IsStepped)
            {
                return Conflict(new { error = "not a stepped host — start it with `dale dev --stepped` to step the virtual clock by hand", reason = "notStepped" });
            }

            if (_runs.HasActiveRun)
            {
                return Conflict(new { error = "a scenario run is driving the clock — stepping is unavailable until it finishes", reason = "runActive" });
            }

            return null;
        }
    }
}