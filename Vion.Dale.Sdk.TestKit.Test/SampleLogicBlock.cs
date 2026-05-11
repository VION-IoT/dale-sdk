using System;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [Service("SampleService")]
    public class SampleLogicBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Power", Unit = "kW")]
        public double Power { get; set; }

        [ServiceProperty]
        public int Counter { get; set; }

        [ServiceMeasuringPoint(Title = "Temperature", Unit = "°C")]
        public double Temperature { get; private set; }

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(5.0)]
        public void OnPeriodicUpdate()
        {
            Counter++;
        }

        public void SetTemperature(double value)
        {
            Temperature = value;
        }

        /// <summary>
        ///     Simulates a two-phase pattern: schedule a delayed action that updates Power.
        /// </summary>
        public void ScheduleDelayedPowerUpdate(double value)
        {
            InvokeSynchronizedAfter(() => Power = value, TimeSpan.FromMilliseconds(500));
        }

        protected override void Ready()
        {
        }
    }
}
