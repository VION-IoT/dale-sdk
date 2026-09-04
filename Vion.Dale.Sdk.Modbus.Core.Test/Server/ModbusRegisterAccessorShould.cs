using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Server
{
    [TestClass]
    public class ModbusRegisterAccessorShould
    {
        private byte[] _buffer = null!;

        private ModbusRegisterAccessor _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _buffer = new byte[40]; // 20 registers
            _sut = new ModbusRegisterAccessor(() => _buffer, 20, ModbusServerArea.HoldingRegisters, new ModbusDataConverter(new BitConverterProxy()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void ReadUShortFromWireOrderBytes()
        {
            // Arrange
            _buffer[0] = 0x12;
            _buffer[1] = 0x34;

            // Act & Assert
            Assert.AreEqual((ushort)0x1234, _sut.ReadAsUShort(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void WriteUShortAsWireOrderBytes()
        {
            // Arrange

            // Act
            _sut.WriteAsUShort(1, 0xBEEF);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0xBE, 0xEF }, new[] { _buffer[2], _buffer[3] });
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void ReadIntHighWordFirstByDefault()
        {
            // Arrange
            // 0x12345678 as MswToLsw: register 0 = 0x1234, register 1 = 0x5678
            _buffer[0] = 0x12;
            _buffer[1] = 0x34;
            _buffer[2] = 0x56;
            _buffer[3] = 0x78;

            // Act & Assert
            Assert.AreEqual(0x12345678, _sut.ReadAsInt(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void ReadIntLowWordFirstWhenRequested()
        {
            // Arrange
            // 0x12345678 as LswToMsw: register 0 = 0x5678, register 1 = 0x1234 (Beckhoff/VGT layout)
            _buffer[0] = 0x56;
            _buffer[1] = 0x78;
            _buffer[2] = 0x12;
            _buffer[3] = 0x34;

            // Act & Assert
            Assert.AreEqual(0x12345678, _sut.ReadAsInt(0, wordOrder: WordOrder32.LswToMsw));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void WriteIntLowWordFirstWhenRequested()
        {
            // Arrange

            // Act
            _sut.WriteAsInt(0, 0x12345678, wordOrder: WordOrder32.LswToMsw);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x56, 0x78, 0x12, 0x34 }, new[] { _buffer[0], _buffer[1], _buffer[2], _buffer[3] });
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void RoundTripEveryTypedPair()
        {
            // Arrange
            _sut.WriteAsShort(0, -1234);
            Assert.AreEqual((short)-1234, _sut.ReadAsShort(0));
            _sut.WriteAsInt(0, -123456, wordOrder: WordOrder32.LswToMsw);
            Assert.AreEqual(-123456, _sut.ReadAsInt(0, wordOrder: WordOrder32.LswToMsw));
            _sut.WriteAsUInt(2, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, _sut.ReadAsUInt(2));
            _sut.WriteAsFloat(4, 3.5f);
            Assert.AreEqual(3.5f, _sut.ReadAsFloat(4));
            _sut.WriteAsLong(6, -123456789012345L);
            Assert.AreEqual(-123456789012345L, _sut.ReadAsLong(6));

            // Act
            _sut.WriteAsULong(10, 0xDEADBEEFCAFEF00DUL);
            Assert.AreEqual(0xDEADBEEFCAFEF00DUL, _sut.ReadAsULong(10));

            // Assert
            _sut.WriteAsDouble(14, 2.25);
            Assert.AreEqual(2.25, _sut.ReadAsDouble(14));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void RoundTripLongAcrossWordOrders()
        {
            // Act & Assert
            foreach (var wordOrder in new[] { WordOrder64.ABCD, WordOrder64.DCBA, WordOrder64.CDAB, WordOrder64.BADC })
            {
                _sut.WriteAsLong(0, 0x0123456789ABCDEFL, wordOrder: wordOrder);
                Assert.AreEqual(0x0123456789ABCDEFL, _sut.ReadAsLong(0, wordOrder: wordOrder), $"word order {wordOrder}");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.7")]
        public void RoundTripStringsPaddedToRegisterBoundary()
        {
            // Arrange

            // Act
            _sut.WriteAsString(0, "VGT");

            // Assert
            Assert.AreEqual("VGT\0", _sut.ReadAsString(0, 2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.7")]
        public void WriteStringsInNaturalWireOrder()
        {
            // Arrange

            // Act
            // Wire-byte assertion independent of ReadAsString, so an inverted byte swap cannot cancel out:
            // string bytes go onto the wire in natural sequential order, exactly like the client's string methods.
            _sut.WriteAsString(0, "VGT");

            // Assert
            CollectionAssert.AreEqual(new[] { (byte)'V', (byte)'G', (byte)'T', (byte)0 }, new[] { _buffer[0], _buffer[1], _buffer[2], _buffer[3] });
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.7")]
        public void ReadStringsInNaturalWireOrder()
        {
            // Arrange
            _buffer[0] = (byte)'A';
            _buffer[1] = (byte)'B';

            // Act & Assert
            Assert.AreEqual("AB", _sut.ReadAsString(0, 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.1")]
        public void RoundTripRawBytes()
        {
            // Arrange

            // Act
            _sut.WriteRaw(18, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, _sut.ReadRaw(18, 2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.3")]
        public void RejectAccessOutsideExtent()
        {
            // Act & Assert
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.ReadAsUShort(20));
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.ReadAsInt(19)); // needs 2 registers
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.WriteAsDouble(17, 1.0)); // needs 4 registers
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.WriteRaw(19, new byte[4]));
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.ReadRaw(0, 21));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.4")]
        public void RejectEmptyReadOrWriteAsArgumentFault()
        {
            // Act & Assert
            // An empty payload is not an address outside the extent, and saying so sends the author to check
            // extents that are fine.
            Assert.ThrowsExactly<ArgumentException>(() => _sut.ReadRaw(0, 0));
            Assert.ThrowsExactly<ArgumentException>(() => _sut.ReadAsString(0, 0));
            Assert.ThrowsExactly<ArgumentException>(() => _sut.WriteRaw(0, []));
            Assert.ThrowsExactly<ArgumentException>(() => _sut.WriteAsString(0, string.Empty));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.7")]
        public void RejectOddRawByteCounts()
        {
            // Act & Assert
            Assert.ThrowsExactly<ArgumentException>(() => _sut.WriteRaw(0, new byte[3]));
        }
    }
}