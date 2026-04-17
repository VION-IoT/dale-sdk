using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    public class SampleLogicBlock : LogicBlockBase
    {
        public IDigitalInput DigitalInput { get; set; } = null!;

        public IDigitalOutput DigitalOutput { get; set; } = null!;

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            DigitalInput.InputChanged += (_, value) => DigitalOutput.Set(value);
        }
    }
}