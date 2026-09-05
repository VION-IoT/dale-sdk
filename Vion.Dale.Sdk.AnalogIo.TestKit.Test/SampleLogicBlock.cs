using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     The consumer-side fixture: it doubles what its input reports onto its output, and records what
    ///     its output's own change event carried. The mirror of the digital kit's fixture.
    /// </summary>
    public class SampleLogicBlock : LogicBlockBase
    {
        public IAnalogInput AnalogInput { get; set; } = null!;

        public IAnalogOutput AnalogOutput { get; set; } = null!;

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>The last value the output's own change event carried, so a raise on it is observable.</summary>
        public double LastOutputConfirmation { get; private set; }

        protected override void Ready()
        {
            AnalogOutput.OutputChanged += (_, value) => LastOutputConfirmation = value;
            AnalogInput.InputChanged += (_, value) => AnalogOutput.Set(value * 2);
        }
    }
}
