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

        public SampleModbusTcpBlock(ILogicBlockModbusTcpClient client, ILogger logger) : base(logger)
        {
            _client = client;
            _client.IpAddress = "127.0.0.1";
            _client.IsEnabled = true;
        }

        [ServiceProperty]
        public uint Power { get; private set; }

        // Plain property (not a ServiceProperty — Exception isn't in the supported type set).
        public Exception? LastReadError { get; private set; }

        /// <summary>Issues a 1×UInt32 holding-register read at address 40000, unit 1, using MswToLsw word order.</summary>
        public void ReadPowerOnce()
        {
            _client.ReadHoldingRegistersAsUInt(unitIdentifier: 1,
                                               startingAddress: 40000,
                                               count: 1,
                                               dispatcher: this,
                                               successCallback: values => Power = values[0],
                                               errorCallback: ex => LastReadError = ex,
                                               byteOrder: ByteOrder.MsbToLsb,
                                               wordOrder: WordOrder32.MswToLsw);
        }

        /// <summary>
        ///     Issues a 1×UInt32 holding-register write at address 40378 (parallels the customer's
        ///     active-power-limit setpoint use case). The fake records the encoded bytes; tests can
        ///     read them back from <c>Proxy.WriteHistory</c> to verify the wire format.
        /// </summary>
        public void WriteActivePowerLimit(uint value, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            _client.WriteMultipleHoldingRegistersAsUInt(unitIdentifier: 1,
                                                        startingAddress: 40378,
                                                        values: new[] { value },
                                                        dispatcher: this,
                                                        successCallback: null,
                                                        errorCallback: ex => LastReadError = ex,
                                                        byteOrder: ByteOrder.MsbToLsb,
                                                        wordOrder: wordOrder);
        }

        protected override void Ready()
        {
        }
    }
}
