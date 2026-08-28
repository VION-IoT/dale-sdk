using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    /// <summary>
    ///     A simulator block: it binds the two provider faces, confirms back what the output commanded, and
    ///     drives the same value onto the input it provides.
    /// </summary>
    public class SampleProviderLogicBlock : LogicBlockBase
    {
        public IDigitalOutputProvider DigitalOutputProvider { get; set; } = null!;

        public IDigitalInputProvider DigitalInputProvider { get; set; } = null!;

        public SampleProviderLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            DigitalOutputProvider.SetReceived += (_, value) =>
                                                 {
                                                     DigitalOutputProvider.Confirm(value);
                                                     DigitalInputProvider.Drive(value);
                                                 };
        }
    }
}