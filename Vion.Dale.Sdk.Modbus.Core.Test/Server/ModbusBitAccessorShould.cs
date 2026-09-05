using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Server
{
    [TestClass]
    public class ModbusBitAccessorShould
    {
        private byte[] _buffer = null!;

        private ModbusBitAccessor _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _buffer = new byte[2];
            _sut = new ModbusBitAccessor(() => _buffer, 10, ModbusServerArea.DiscreteInputs);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.6")]
        public void ReadBitsPackedLeastSignificantFirst()
        {
            // Arrange
            _buffer[0] = 0b0000_0101; // bits 0 and 2 set
            _buffer[1] = 0b0000_0010; // bit 9 set

            // Act & Assert
            Assert.IsTrue(_sut.Read(0));
            Assert.IsFalse(_sut.Read(1));
            Assert.IsTrue(_sut.Read(2));
            Assert.IsTrue(_sut.Read(9));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.6")]
        public void WriteAndClearBits()
        {
            // Arrange
            _sut.Write(3, true);
            Assert.AreEqual(0b0000_1000, _buffer[0]);

            // Act & Assert
            _sut.Write(3, false);
            Assert.AreEqual(0, _buffer[0]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.6")]
        public void LeaveNeighboringBitsUntouched()
        {
            // Arrange
            _buffer[0] = 0b1111_1111;

            // Act
            _sut.Write(4, false);

            // Assert
            Assert.AreEqual(0b1110_1111, _buffer[0]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.3")]
        public void RejectAccessOutsideExtent()
        {
            // Act & Assert
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Read(10));
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Write(10, true));
        }
    }
}