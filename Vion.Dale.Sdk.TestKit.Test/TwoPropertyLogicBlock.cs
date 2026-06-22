using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     Two independent throttled service properties — proves VerifyServicePropertyEmitted
    ///     filters by property identifier and never cross-counts.
    /// </summary>
    public class TwoPropertyLogicBlock : LogicBlockBase
    {
        [ServiceProperty(MinInterval = "250ms")]
        public double Power { get; set; }

        [ServiceProperty(MinInterval = "250ms")]
        public double Rate { get; set; }

        public TwoPropertyLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}
