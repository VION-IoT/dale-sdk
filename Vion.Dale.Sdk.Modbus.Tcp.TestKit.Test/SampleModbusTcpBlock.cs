using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     Minimal LogicBlock harness for exercising the FakeModbusTcpClientProxy + SynchronousRequestQueue
    ///     round-trip. Issues a single typed read on demand; the decoded result lands on <see cref="Power" />.
    /// </summary>
    public sealed class SampleModbusTcpBlock : LogicBlockBase
    {
        private readonly ILogicBlockModbusTcpClient _client;

        [ServiceProperty]
        public uint Power { get; private set; }

        // Plain property (not a ServiceProperty — Exception isn't in the supported type set).
        public Exception? LastReadError { get; private set; }

        public SampleModbusTcpBlock(ILogicBlockModbusTcpClient client, ILogger logger) : base(logger)
        {
            _client = client;
            _client.IpAddress = "127.0.0.1";
            _client.IsEnabled = true;
        }

        /// <summary>Issues a 1×UInt32 holding-register read at address 40000, unit 1, using MswToLsw word order.</summary>
        public void ReadPowerOnce()
        {
            _client.ReadHoldingRegistersAsUInt(1,
                                               40000,
                                               1,
                                               this,
                                               values => Power = values[0],
                                               ex => LastReadError = ex);
        }

        /// <summary>
        ///     Issues a 1×UInt32 holding-register write at address 40378 (parallels the customer's
        ///     active-power-limit setpoint use case). The fake records the encoded bytes; tests can
        ///     read them back from <c>Proxy.WriteHistory</c> to verify the wire format.
        /// </summary>
        public void WriteActivePowerLimit(uint value, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            _client.WriteMultipleHoldingRegistersAsUInt(1,
                                                        40378,
                                                        new[] { value },
                                                        this,
                                                        null,
                                                        ex => LastReadError = ex,
                                                        ByteOrder.MsbToLsb,
                                                        wordOrder);
        }

        protected override void Ready()
        {
        }
    }
}