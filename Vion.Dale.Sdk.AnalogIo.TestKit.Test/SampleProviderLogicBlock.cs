using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     A simulator block: it binds the two provider faces, confirms back what the output commanded, and
    ///     drives the same value onto the input it provides.
    /// </summary>
    public class SampleProviderLogicBlock : LogicBlockBase
    {
        public IAnalogOutputProvider AnalogOutputProvider { get; set; } = null!;

        public IAnalogInputProvider AnalogInputProvider { get; set; } = null!;

        public SampleProviderLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            AnalogOutputProvider.SetReceived += (_, value) =>
                                                {
                                                    AnalogOutputProvider.Confirm(value);
                                                    AnalogInputProvider.Drive(value);
                                                };
        }
    }
}