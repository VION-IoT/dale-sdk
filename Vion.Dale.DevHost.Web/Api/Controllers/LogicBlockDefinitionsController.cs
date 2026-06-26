using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     The block catalog over HTTP (RFC 0013 Phase 1): every <c>LogicBlockBase</c> type the running DevHost
    ///     references, each projected to its per-interface matching metadata via <see cref="LogicBlockDefinition" />.
    ///     The server exposes the introspection DATA; a later client phase does the wiring logic.
    /// </summary>
    [ApiController]
    [Route("api/logic-block-definitions")]
    public class LogicBlockDefinitionsController : ControllerBase
    {
        private readonly DevBlockCatalog _catalog;

        public LogicBlockDefinitionsController(DevBlockCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>Every catalog block type, projected to its reflection-built matching metadata.</summary>
        [HttpGet]
        public IActionResult List()
        {
            return Ok(new { definitions = _catalog.Types.Select(LogicBlockDefinition.FromType).ToList() });
        }
    }
}
