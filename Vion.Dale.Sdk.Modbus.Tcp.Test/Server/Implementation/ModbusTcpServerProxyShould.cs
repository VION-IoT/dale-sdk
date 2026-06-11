using FluentModbus;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server.Implementation
{
    [TestClass]
    public class ModbusTcpServerProxyShould
    {
        private static readonly ModbusServerAreaExtents Extents = new(HoldingRegisterCount: 10, InputRegisterCount: 20, CoilCount: 7, DiscreteInputCount: 1);

        [TestMethod]
        [DataRow(ModbusFunctionCode.ReadHoldingRegisters, (ushort)0, (ushort)10, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadHoldingRegisters, (ushort)0, (ushort)11, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.WriteSingleRegister, (ushort)9, (ushort)1, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.WriteSingleRegister, (ushort)10, (ushort)1, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.WriteMultipleRegisters, (ushort)8, (ushort)2, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.WriteMultipleRegisters, (ushort)9, (ushort)2, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.ReadWriteMultipleRegisters, (ushort)0, (ushort)10, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadInputRegisters, (ushort)0, (ushort)20, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadInputRegisters, (ushort)20, (ushort)1, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.ReadCoils, (ushort)0, (ushort)7, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadCoils, (ushort)7, (ushort)1, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.WriteSingleCoil, (ushort)6, (ushort)1, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.WriteSingleCoil, (ushort)7, (ushort)1, ModbusExceptionCode.IllegalDataAddress)]
        [DataRow(ModbusFunctionCode.WriteMultipleCoils, (ushort)0, (ushort)7, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadDiscreteInputs, (ushort)0, (ushort)1, ModbusExceptionCode.OK)]
        [DataRow(ModbusFunctionCode.ReadDiscreteInputs, (ushort)1, (ushort)1, ModbusExceptionCode.IllegalDataAddress)]
        public void ValidateRequestsAgainstTheDeclaredExtents(ModbusFunctionCode functionCode, ushort startingAddress, ushort quantity, ModbusExceptionCode expected)
        {
            Assert.AreEqual(expected, ModbusTcpServerProxy.ValidateRequest(functionCode, startingAddress, quantity, Extents));
        }

        [TestMethod]
        public void TreatSingleWriteQuantityZeroAsOne()
        {
            // FluentModbus reports single-value writes with quantity 0 in some paths — they touch exactly one address.
            Assert.AreEqual(ModbusExceptionCode.OK, ModbusTcpServerProxy.ValidateRequest(ModbusFunctionCode.WriteSingleRegister, 9, 0, Extents));
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, ModbusTcpServerProxy.ValidateRequest(ModbusFunctionCode.WriteSingleCoil, 7, 0, Extents));
        }

        [TestMethod]
        public void LeaveUnsupportedFunctionCodesToFluentModbus()
        {
            Assert.AreEqual(ModbusExceptionCode.OK, ModbusTcpServerProxy.ValidateRequest(ModbusFunctionCode.ReadFifoQueue, 1234, 1, Extents));
        }
    }
}
