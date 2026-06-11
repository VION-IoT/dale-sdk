using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Server
{
    [TestClass]
    public class ModbusServerAreaExtentsShould
    {
        private static readonly ModbusServerAreaExtents Extents = new(HoldingRegisterCount: 10, InputRegisterCount: 20, CoilCount: 7, DiscreteInputCount: 1);

        [TestMethod]
        [DataRow(ModbusServerArea.HoldingRegisters, (ushort)0, 10u, true)]
        [DataRow(ModbusServerArea.HoldingRegisters, (ushort)9, 1u, true)]
        [DataRow(ModbusServerArea.HoldingRegisters, (ushort)9, 2u, false)]
        [DataRow(ModbusServerArea.HoldingRegisters, (ushort)10, 1u, false)]
        [DataRow(ModbusServerArea.InputRegisters, (ushort)0, 20u, true)]
        [DataRow(ModbusServerArea.Coils, (ushort)6, 1u, true)]
        [DataRow(ModbusServerArea.Coils, (ushort)7, 1u, false)]
        [DataRow(ModbusServerArea.DiscreteInputs, (ushort)0, 1u, true)]
        [DataRow(ModbusServerArea.DiscreteInputs, (ushort)1, 1u, false)]
        public void DecideWhetherARangeIsCovered(ModbusServerArea area, ushort startingAddress, uint quantity, bool expected)
        {
            Assert.AreEqual(expected, Extents.Covers(area, startingAddress, quantity));
        }

        [TestMethod]
        public void RejectZeroQuantity()
        {
            Assert.IsFalse(Extents.Covers(ModbusServerArea.HoldingRegisters, 0, 0));
        }

        [TestMethod]
        public void RejectRangesOnUnservedAreas()
        {
            var none = new ModbusServerAreaExtents(0, 0, 0, 0);
            Assert.IsFalse(none.Covers(ModbusServerArea.HoldingRegisters, 0, 1));
        }

        [TestMethod]
        public void CoverOffsetBasedMapsUpTo0X8000()
        {
            var stStyle = new ModbusServerAreaExtents(HoldingRegisterCount: 0x800A, InputRegisterCount: 0, CoilCount: 0, DiscreteInputCount: 0);
            Assert.IsTrue(stStyle.Covers(ModbusServerArea.HoldingRegisters, 0x8000, 10));
            Assert.IsFalse(stStyle.Covers(ModbusServerArea.HoldingRegisters, 0x8000, 11));
        }
    }
}
