using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
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

        /// <summary>
        ///     Simulates the "schedule a follow-up on next dispatch" pattern used by production callbacks
        ///     (e.g. Modbus / HTTP response handlers, or contract-update bypass handlers): the action is
        ///     queued via <see cref="LogicBlockBase.InvokeSynchronized" /> with no delay.
        /// </summary>
        public void ScheduleImmediatePowerUpdate(double value)
        {
            InvokeSynchronized(() => Power = value);
        }

        protected override void Ready()
        {
        }
    }
}