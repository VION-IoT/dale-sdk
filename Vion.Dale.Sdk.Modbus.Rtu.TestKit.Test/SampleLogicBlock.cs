using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

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

        /// <summary>The receipt of the last read that completed, whichever way it ended.</summary>
        public ModbusReceipt? LastReadReceipt { get; private set; }

        /// <summary>The receipt of the last write that completed, whichever way it ended.</summary>
        public ModbusReceipt? LastWriteReceipt { get; private set; }

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        public void ReadVoltages()
        {
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             VoltagesAddress,
                                             3,
                                             this,
                                             (values, receipt) =>
                                             {
                                                 LastVoltages = values;
                                                 LastReadReceipt = receipt;
                                             },
                                             (error, receipt) =>
                                             {
                                                 LastError = error;
                                                 LastReadReceipt = receipt;
                                             });
        }

        public void ReadCurrents()
        {
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             CurrentsAddress,
                                             3,
                                             this,
                                             (values, receipt) =>
                                             {
                                                 LastCurrents = values;
                                                 LastReadReceipt = receipt;
                                             },
                                             (error, receipt) =>
                                             {
                                                 LastError = error;
                                                 LastReadReceipt = receipt;
                                             });
        }

        public void WriteSetpoint(short value)
        {
            Modbus.WriteSingleHoldingRegister(UnitId,
                                              SetpointAddress,
                                              value,
                                              this,
                                              receipt =>
                                              {
                                                  WriteSuccessCount++;
                                                  LastWriteReceipt = receipt;
                                              },
                                              (error, receipt) =>
                                              {
                                                  LastError = error;
                                                  LastWriteReceipt = receipt;
                                              });
        }

        protected override void Ready()
        {
            Modbus.IsEnabled = true;
        }
    }
}