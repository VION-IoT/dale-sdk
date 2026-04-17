using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    [Service("SampleService")]
    public class SampleLogicBlock : LogicBlockBase
    {
        private double _lastAnalogValue;

        public IAnalogInput AnalogInput { get; set; } = null!;

        public IAnalogOutput AnalogOutput { get; set; } = null!;

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            AnalogInput.InputChanged += (_, value) =>
                                        {
                                            _lastAnalogValue = value;
                                            AnalogOutput.Set(value * 2);
                                        };
        }
    }
}