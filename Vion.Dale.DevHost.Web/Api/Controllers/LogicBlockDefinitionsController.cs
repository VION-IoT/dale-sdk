using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    /// <summary>
    ///     The block catalog over HTTP (RFC 0013 Phase 1): every <c>LogicBlockBase</c> type the running DevHost
    ///     references, each projected to its per-interface matching metadata + <c>[InstantiationParameter]</c>
    ///     definitions via <see cref="LogicBlockDefinition" />. The server exposes the introspection DATA; the
    ///     client phase does the wiring logic and the parameter editing.
    /// </summary>
    [ApiController]
    [Route("api/logic-block-definitions")]
    public class LogicBlockDefinitionsController : ControllerBase
    {
        // A minimal provider satisfying the logger dependency every LogicBlockBase constructor takes, so a block can
        // be instantiated here purely to read its [InstantiationParameter] C# initializer defaults — the block
        // registrations live in a separate runtime container, not this web request's provider.
        private static readonly IServiceProvider ParameterDefaultProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                                   .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                                   .AddSingleton<ILogger>(NullLogger.Instance)
                                                                                                   .BuildServiceProvider();

        private readonly DevBlockCatalog _catalog;

        public LogicBlockDefinitionsController(DevBlockCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>Every catalog block type, projected to its reflection-built matching metadata + parameter definitions.</summary>
        [HttpGet]
        public IActionResult List()
        {
            return Ok(new { definitions = _catalog.Types.Select(BuildDefinition).ToList() });
        }

        private static LogicBlockDefinition BuildDefinition(Type type)
        {
            object? instance = null;
            try
            {
                // Instantiate solely to read parameter defaults; a C# `init` initializer is not otherwise reflectable.
                instance = ActivatorUtilities.CreateInstance(ParameterDefaultProvider, type);
            }
            catch
            {
                // A block whose constructor needs more than a logger still lists — without parameter defaults.
            }

            try
            {
                return LogicBlockDefinition.FromType(type, instance);
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }
        }
    }
}