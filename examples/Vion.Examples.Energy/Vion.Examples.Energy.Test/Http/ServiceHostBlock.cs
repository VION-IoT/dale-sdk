using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Test.Http
{
    /// <summary>
    ///     The block the two HTTP services are called on behalf of. It does nothing itself: the services take a block as the
    ///     dispatcher their callbacks run on, and this is the smallest one the TestKit context can host.
    /// </summary>
    public sealed class ServiceHostBlock : LogicBlockBase
    {
        public ServiceHostBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}