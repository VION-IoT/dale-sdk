using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    /// <summary>
    ///     The consumer-side fixture: it echoes what its input reports onto its output, and records what
    ///     its output's own change event carried. The mirror of the analog kit's fixture.
    /// </summary>
    public class SampleLogicBlock : LogicBlockBase
    {
        public IDigitalInput DigitalInput { get; set; } = null!;

        public IDigitalOutput DigitalOutput { get; set; } = null!;

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>The last value the output's own change event carried, so a raise on it is observable.</summary>
        public bool LastOutputConfirmation { get; private set; }

        protected override void Ready()
        {
            DigitalOutput.OutputChanged += (_, value) => LastOutputConfirmation = value;
            DigitalInput.InputChanged += (_, value) => DigitalOutput.Set(value);
        }
    }
}