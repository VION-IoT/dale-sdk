using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     A block with a throttled service property (250 ms min-interval, the attribute default).
    ///     Used to prove that <c>WithEmissionPolicy(FromAttributes)</c> forces the RFC 0004 gate on
    ///     even under the TestKit's fake clock, while the default (Off) leaves every assignment
    ///     flowing straight through as a change.
    /// </summary>
    public class ThrottledLogicBlock : LogicBlockBase
    {
        [ServiceProperty(MinInterval = "250ms")]
        public double Power { get; set; }

        public ThrottledLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}
