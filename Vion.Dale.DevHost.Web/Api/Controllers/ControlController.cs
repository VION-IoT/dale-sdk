using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     Read/observe endpoints over the headless control surface (RFC 0003), for external tools and agents:
    ///     topology, live state, captured logs, and the inter-block message tap. The set endpoints already live
    ///     on <see cref="DevHostController" /> (<c>/api/dale/property</c>, <c>/api/hal/...</c>). Additive — does
    ///     not change any existing route, the SPA, or SignalR.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class ControlController : ControllerBase
    {
        private readonly IDevHostControl _control;

        public ControlController(IDevHostControl control)
        {
            _control = control;
        }

        [HttpGet("blocks")]
        public ActionResult GetBlocks()
        {
            return Ok(_control.ListBlocks());
        }

        [HttpGet("state/{blockIdOrName}")]
        public ActionResult GetState(string blockIdOrName)
        {
            return Ok(_control.GetAllProperties(blockIdOrName));
        }

        [HttpGet("state/{blockIdOrName}/{propertyName}")]
        public ActionResult GetState(string blockIdOrName, string propertyName)
        {
            return Ok(new { blockIdOrName, propertyName, value = _control.GetProperty(blockIdOrName, propertyName) });
        }

        [HttpGet("logs/recent")]
        public ActionResult GetRecentLogs([FromQuery] int max = 500)
        {
            // Project to a stable wire shape (LogLine fields are all primitives/strings).
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

        [HttpGet("messages")]
        public ActionResult GetMessages([FromQuery] string? block = null)
        {
            // The raw message instance can be an arbitrary CLR type; project to a serialization-safe shape
            // (actor, type, a best-effort string, timestamp) rather than risk serializing the object itself.
            var messages = _control.RecordedMessages(block)
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
