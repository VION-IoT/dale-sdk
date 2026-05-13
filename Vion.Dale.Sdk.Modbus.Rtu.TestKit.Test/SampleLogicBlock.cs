using System;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    public class SampleLogicBlock : LogicBlockBase
    {
        public const ushort VoltagesAddress = 0;

        public const ushort CurrentsAddress = 6;

        public const ushort SetpointAddress = 100;

        private const int UnitId = 1;

        [ServiceProviderContractBinding(Identifier = "Modbus", DefaultName = "Sample Modbus RTU")]
        public IModbusRtu Modbus { get; set; } = null!;

        public float[] LastVoltages { get; private set; } = Array.Empty<float>();

        public float[] LastCurrents { get; private set; } = Array.Empty<float>();

        public int WriteSuccessCount { get; private set; }

        public Exception? LastError { get; private set; }

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            Modbus.IsEnabled = true;
        }

        public void ReadVoltages()
        {
            Modbus.ReadInputRegistersAsFloat(UnitId, VoltagesAddress, 3, values => LastVoltages = values, error => LastError = error);
        }

        public void ReadCurrents()
        {
            Modbus.ReadInputRegistersAsFloat(UnitId, CurrentsAddress, 3, values => LastCurrents = values, error => LastError = error);
        }

        public void WriteSetpoint(short value)
        {
            Modbus.WriteSingleHoldingRegister(UnitId, SetpointAddress, value, () => WriteSuccessCount++, error => LastError = error);
        }
    }
}
