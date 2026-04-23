using System;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    [TestClass]
    public class ModbusResponseBuilderShould
    {
        [TestMethod]
        public void EncodeSingleFloatAsFourBigEndianBytes()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromFloats(1.0f);

            // Assert
            Assert.HasCount(4, bytes);
            CollectionAssert.AreEqual(BigEndianBytes(BitConverter.GetBytes(1.0f)), bytes);
        }

        [TestMethod]
        public void EncodeMultipleFloatsInOrder()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromFloats(1.0f, 2.0f, 3.0f);

            // Assert
            Assert.HasCount(12, bytes);
            AssertSliceEquals(bytes, 0, BigEndianBytes(BitConverter.GetBytes(1.0f)));
            AssertSliceEquals(bytes, 4, BigEndianBytes(BitConverter.GetBytes(2.0f)));
            AssertSliceEquals(bytes, 8, BigEndianBytes(BitConverter.GetBytes(3.0f)));
        }

        [TestMethod]
        public void EncodeShortAsTwoBigEndianBytes()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromShorts(0x1234);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, bytes);
        }

        [TestMethod]
        public void EncodeUShortAsTwoBigEndianBytes()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromUShorts(0xABCD);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, bytes);
        }

        [TestMethod]
        public void EncodeIntAsFourBigEndianBytes()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromInts(0x01020304);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04 }, bytes);
        }

        [TestMethod]
        public void EncodeDoubleAsEightBigEndianBytes()
        {
            // Act
            var bytes = ModbusResponseBuilder.FromDoubles(1.0);

            // Assert
            Assert.HasCount(8, bytes);
            CollectionAssert.AreEqual(BigEndianBytes(BitConverter.GetBytes(1.0)), bytes);
        }

        [TestMethod]
        public void PackBoolsLsbFirstWithinEachByte()
        {
            // Act
            // Expected layout per Modbus coil packing (LSB first):
            // bits 0..7 → byte 0: 1,0,1,0,0,0,0,1 → 0b10000101 = 0x85
            // bits 8,9  → byte 1: 1,1              → 0b00000011 = 0x03
            var bytes = ModbusResponseBuilder.FromBools(true, false, true, false, false, false, false, true, true, true);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x85, 0x03 }, bytes);
        }

        [TestMethod]
        [DataRow(1, 1)]
        [DataRow(7, 1)]
        [DataRow(8, 1)]
        [DataRow(9, 2)]
        [DataRow(16, 2)]
        [DataRow(17, 3)]
        public void AllocateOneByteForEveryEightBools(int boolCount, int expectedByteCount)
        {
            // Act
            var bytes = ModbusResponseBuilder.FromBools(new bool[boolCount]);

            // Assert
            Assert.HasCount(expectedByteCount, bytes);
        }

        [TestMethod]
        public void ReturnEmptyArrayForEmptyInput()
        {
            // Act / Assert
            Assert.IsEmpty(ModbusResponseBuilder.FromFloats());
            Assert.IsEmpty(ModbusResponseBuilder.FromShorts());
            Assert.IsEmpty(ModbusResponseBuilder.FromUShorts());
            Assert.IsEmpty(ModbusResponseBuilder.FromInts());
            Assert.IsEmpty(ModbusResponseBuilder.FromDoubles());
            Assert.IsEmpty(ModbusResponseBuilder.FromBools());
        }

        private static byte[] BigEndianBytes(byte[] littleEndianCandidate)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(littleEndianCandidate);
            }

            return littleEndianCandidate;
        }

        private static void AssertSliceEquals(byte[] source, int offset, byte[] expected)
        {
            var slice = new byte[expected.Length];
            Buffer.BlockCopy(source, offset, slice, 0, expected.Length);
            CollectionAssert.AreEqual(expected, slice);
        }
    }
}
