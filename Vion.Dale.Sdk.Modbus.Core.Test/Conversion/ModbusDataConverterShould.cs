using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Moq;

// ReSharper disable UseUtf8StringLiteral - Testing conversion from raw byte array (as received from Modbus) rather than UTF-8 string literal

namespace Vion.Dale.Sdk.Modbus.Core.Test.Conversion
{
    [TestClass]
    public class ModbusDataConverterShould
    {
        private readonly Mock<IBitConverterProxy> _bitConverterProxy = new();

        private ModbusDataConverter _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new ModbusDataConverter(_bitConverterProxy.Object);
        }

        [TestMethod]
        public void ThrowExceptionWhenCountIsZero()
        {
            // Arrange
            const uint count = 0;
            const int bytesPerCount = 2;

            // Act / Assert
            Assert.Throws<InvalidCountException>(() => _sut.ConvertCountToQuantity(count, bytesPerCount));
        }

        [TestMethod]
        [DataRow(2, DisplayName = "2 bytes per count (16-bit)")]
        [DataRow(4, DisplayName = "4 bytes per count (32-bit)")]
        [DataRow(8, DisplayName = "8 bytes per count (64-bit)")]
        public void ThrowExceptionWhenConvertedQuantityExceedsMaximumValue(int bytesPerCount)
        {
            // Arrange
            const int bytesPerRegister = 2;
            var registersPerCount = bytesPerCount / bytesPerRegister;
            var maxCount = ushort.MaxValue / registersPerCount;
            var count = (uint)(maxCount + 1);

            // Act / Assert
            Assert.Throws<InvalidCountException>(() => _sut.ConvertCountToQuantity(count, bytesPerCount));
        }

        [TestMethod]
        [DataRow(4, 2, 4, DisplayName = "4 count with 16-bit registers")]
        [DataRow(4, 4, 8, DisplayName = "4 count with 32-bit registers")]
        [DataRow(4, 8, 16, DisplayName = "4 count with 64-bit registers")]
        public void ConvertCountToQuantityBasedOnBytesPerCount(int count, int bytesPerCount, int expectedQuantity)
        {
            // Arrange

            // Act
            var actualQuantity = _sut.ConvertCountToQuantity((uint)count, bytesPerCount);

            // Assert
            Assert.AreEqual(expectedQuantity, actualQuantity);
        }

        [TestMethod]
        public void ThrowExceptionWhenByteOrderIsUnsupported()
        {
            // Arrange
            const ByteOrder unsupportedByteOrder = (ByteOrder)999;

            // Act / Assert
            Assert.Throws<UnsupportedByteOrderException>(() => _sut.SwapBytes(Array.Empty<byte>(), unsupportedByteOrder));
        }

        [TestMethod]
        [DataRow(ByteOrder.LsbToMsb, true, DisplayName = "Little-endian byte order on little-endian system")]
        [DataRow(ByteOrder.MsbToLsb, false, DisplayName = "Big-endian byte order on big-endian system")]
        public void NotSwapBytesWhenByteOrderMatchesSystemEndianness(ByteOrder byteOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes = [0x01, 0x02, 0xA1, 0xA2];

            // Act
            _sut.SwapBytes(bytes, byteOrder);

            // Assert
            byte[] expectedBytes = [0x01, 0x02, 0xA1, 0xA2];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        [DataRow(ByteOrder.LsbToMsb, false, DisplayName = "Little-endian byte order on big-endian system")]
        [DataRow(ByteOrder.MsbToLsb, true, DisplayName = "Big-endian byte order on little-endian system")]
        public void SwapBytesWhenByteOrderDoesNotMatchSystemEndianness(ByteOrder byteOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes = [0x02, 0x01, 0xA2, 0xA1];

            // Act
            _sut.SwapBytes(bytes, byteOrder);

            // Assert
            byte[] expectedBytes = [0x01, 0x02, 0xA1, 0xA2];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void ThrowExceptionWhen32BitWordOrderIsUnsupported()
        {
            // Arrange
            const WordOrder32 unsupportedWordOrder = (WordOrder32)999;

            // Act / Assert
            Assert.Throws<UnsupportedWordOrder32Exception>(() => _sut.SwapWords(Array.Empty<byte>(), unsupportedWordOrder));
        }

        [TestMethod]
        [DataRow(WordOrder32.LswToMsw, true, DisplayName = "Little-endian word order on little-endian system")]
        [DataRow(WordOrder32.MswToLsw, false, DisplayName = "Big-endian word order on big-endian system")]
        public void NotSwap32BitWordsWhenWordOrderMatchesSystemEndianness(WordOrder32 wordOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes = [0x01, 0x02, 0x03, 0x04, 0xA1, 0xA2, 0xA3, 0xA4];

            // Act
            _sut.SwapWords(bytes, wordOrder);

            // Assert
            byte[] expectedBytes = [0x01, 0x02, 0x03, 0x04, 0xA1, 0xA2, 0xA3, 0xA4];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        [DataRow(WordOrder32.LswToMsw, false, DisplayName = "Little-endian word order on big-endian system")]
        [DataRow(WordOrder32.MswToLsw, true, DisplayName = "Big-endian word order on little-endian system")]
        public void Swap32BitWordsWhenWordOrderDoesNotMatchSystemEndianness(WordOrder32 wordOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes = [0x03, 0x04, 0x01, 0x02, 0xA3, 0xA4, 0xA1, 0xA2];

            // Act
            _sut.SwapWords(bytes, wordOrder);

            // Assert
            byte[] expectedBytes = [0x01, 0x02, 0x03, 0x04, 0xA1, 0xA2, 0xA3, 0xA4];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void ThrowExceptionWhen64BitWordOrderIsUnsupported()
        {
            // Arrange
            const WordOrder64 unsupportedWordOrder = (WordOrder64)999;

            // Act / Assert
            Assert.Throws<UnsupportedWordOrder64Exception>(() => _sut.SwapWords(new byte[0x01], unsupportedWordOrder));
        }

        [TestMethod]
        [DataRow(WordOrder64.DCBA, true, DisplayName = "Little-endian word order on little-endian system")]
        [DataRow(WordOrder64.ABCD, false, DisplayName = "Big-endian word order on big-endian system")]
        public void NotSwap64BitWordsWhenWordOrderMatchesSystemEndianness(WordOrder64 wordOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes =
            [
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
            ];

            // Act
            _sut.SwapWords(bytes, wordOrder);

            // Assert
            byte[] expectedBytes =
            [
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        [DataRow(WordOrder64.DCBA, false, DisplayName = "Little-endian word order on big-endian system")]
        [DataRow(WordOrder64.ABCD, true, DisplayName = "Big-endian word order on little-endian system")]
        public void Swap64BitWordsWhenWordOrderDoesNotMatchSystemEndianness(WordOrder64 wordOrder, bool isLittleEndian)
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(isLittleEndian);
            byte[] bytes =
            [
                0x07, 0x08, 0x05, 0x06, 0x03, 0x04, 0x01, 0x02,
                0xA7, 0xA8, 0xA5, 0xA6, 0xA3, 0xA4, 0xA1, 0xA2,
            ];

            // Act
            _sut.SwapWords(bytes, wordOrder);

            // Assert
            byte[] expectedBytes =
            [
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void Swap64BitMidBigEndianWordsToLittleEndianWhenSystemIsLittleEndian()
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(true);
            byte[] bytes =
            [
                0xC1, 0xC2, 0xD1, 0xD2, 0xA1, 0xA2, 0xB1, 0xB2,
                0xC3, 0xC4, 0xD3, 0xD4, 0xA3, 0xA4, 0xB3, 0xB4,
            ];

            // Act
            _sut.SwapWords(bytes, WordOrder64.CDAB);

            // Assert
            byte[] expectedBytes =
            [
                0xD1, 0xD2, 0xC1, 0xC2, 0xB1, 0xB2, 0xA1, 0xA2,
                0xD3, 0xD4, 0xC3, 0xC4, 0xB3, 0xB4, 0xA3, 0xA4,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void Swap64BitMidBigEndianWordsToBigEndianWhenSystemIsBigEndian()
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(false);
            byte[] bytes =
            [
                0xC1, 0xC2, 0xD1, 0xD2, 0xA1, 0xA2, 0xB1, 0xB2,
                0xC3, 0xC4, 0xD3, 0xD4, 0xA3, 0xA4, 0xB3, 0xB4,
            ];

            // Act
            _sut.SwapWords(bytes, WordOrder64.CDAB);

            // Assert
            byte[] expectedBytes =
            [
                0xA1, 0xA2, 0xB1, 0xB2, 0xC1, 0xC2, 0xD1, 0xD2,
                0xA3, 0xA4, 0xB3, 0xB4, 0xC3, 0xC4, 0xD3, 0xD4,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void Swap64BitMidLittleEndianWordsToLittleEndianWhenSystemIsLittleEndian()
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(true);
            byte[] bytes =
            [
                0xB1, 0xB2, 0xA1, 0xA2, 0xD1, 0xD2, 0xC1, 0xC2,
                0xB3, 0xB4, 0xA3, 0xA4, 0xD3, 0xD4, 0xC3, 0xC4,
            ];

            // Act
            _sut.SwapWords(bytes, WordOrder64.BADC);

            // Assert
            byte[] expectedBytes =
            [
                0xD1, 0xD2, 0xC1, 0xC2, 0xB1, 0xB2, 0xA1, 0xA2,
                0xD3, 0xD4, 0xC3, 0xC4, 0xB3, 0xB4, 0xA3, 0xA4,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void Swap64BitMidLittleEndianWordsToBigEndianWhenSystemIsBigEndian()
        {
            // Arrange
            _bitConverterProxy.SetupGet(bitConverter => bitConverter.IsLittleEndian).Returns(false);
            byte[] bytes =
            [
                0xB1, 0xB2, 0xA1, 0xA2, 0xD1, 0xD2, 0xC1, 0xC2,
                0xB3, 0xB4, 0xA3, 0xA4, 0xD3, 0xD4, 0xC3, 0xC4,
            ];

            // Act
            _sut.SwapWords(bytes, WordOrder64.BADC);

            // Assert
            byte[] expectedBytes =
            [
                0xA1, 0xA2, 0xB1, 0xB2, 0xC1, 0xC2, 0xD1, 0xD2,
                0xA3, 0xA4, 0xB3, 0xB4, 0xC3, 0xC4, 0xD3, 0xD4,
            ];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        [DataRow(nameof(ModbusDataConverter.ConvertBytesToString))]
        [DataRow(nameof(ModbusDataConverter.ConvertStringToBytes))]
        public void ThrowExceptionWhenTextEncodingIsUnsupported(string methodName)
        {
            // Arrange
            const TextEncoding unsupportedEncoding = (TextEncoding)999;

            // Act / Assert
            if (methodName == nameof(ModbusDataConverter.ConvertBytesToString))
            {
                Assert.Throws<UnsupportedTextEncodingException>(() => _sut.ConvertBytesToString(Array.Empty<byte>(), unsupportedEncoding));
            }
            else
            {
                Assert.Throws<UnsupportedTextEncodingException>(() => _sut.ConvertStringToBytes(string.Empty, unsupportedEncoding));
            }
        }

        [TestMethod]
        public void ConvertBytesToAsciiString()
        {
            // Arrange
            byte[] bytes = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21];

            // Act
            var actualString = _sut.ConvertBytesToString(bytes, TextEncoding.Ascii);

            // Assert
            const string expectedString = "Hello!";
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        public void ConvertBytesToUtf8String()
        {
            // Arrange
            byte[] bytes = [0x48, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F, 0x21];

            // Act
            var actualString = _sut.ConvertBytesToString(bytes, TextEncoding.Utf8);

            // Assert
            const string expectedString = "Héllo!";
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        public void ConvertBytesToUtf16LeString()
        {
            // Arrange
            byte[] bytes = [0x3D, 0xD8, 0x00, 0xDE];

            // Act
            var actualString = _sut.ConvertBytesToString(bytes, TextEncoding.Utf16Le);

            // Assert
            const string expectedString = "😀";
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        public void ConvertBytesToUtf16BeString()
        {
            // Arrange
            byte[] bytes = [0xD8, 0x3D, 0xDE, 0x00];

            // Act
            var actualString = _sut.ConvertBytesToString(bytes, TextEncoding.Utf16Be);

            // Assert
            const string expectedString = "😀";
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public void ConvertEmptyStringToBytes(TextEncoding encoding)
        {
            // Arrange
            const string value = "";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, encoding);

            // Assert
            byte[] expectedBytes = [];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertAsciiStringToBytes()
        {
            // Arrange
            const string value = "Hello!";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Ascii);

            // Assert
            byte[] expectedBytes = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertUtf8StringToBytes()
        {
            // Arrange
            const string value = "Héllo";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Utf8);

            // Assert
            byte[] expectedBytes = [0x48, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertUtf16LeStringToBytes()
        {
            // Arrange
            const string value = "😀";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Utf16Le);

            // Assert
            byte[] expectedBytes = [0x3D, 0xD8, 0x00, 0xDE];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertUtf16BeStringToBytes()
        {
            // Arrange
            const string value = "😀";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Utf16Be);

            // Assert
            byte[] expectedBytes = [0xD8, 0x3D, 0xDE, 0x00];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertAsciiStringToBytesWithNullByteWhenLengthIsOdd()
        {
            // Arrange
            const string value = "Hello";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Ascii);

            // Assert
            byte[] expectedBytes = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ConvertUtf8StringToBytesWithNullByteWhenLengthIsOdd()
        {
            // Arrange
            const string value = "Héll";

            // Act
            var actualBytes = _sut.ConvertStringToBytes(value, TextEncoding.Utf8);

            // Assert
            byte[] expectedBytes = [0x48, 0xC3, 0xA9, 0x6C, 0x6C, 0x00];
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        [TestMethod]
        public void ThrowExceptionWhenQuantityExceedsAvailableBits()
        {
            // Arrange
            byte[] bytes = [0x0A, 0xAA];
            var quantity = (ushort)(bytes.Length * 8 + 1);

            // Act / Assert
            Assert.Throws<InvalidBitQuantityException>(() => _sut.ConvertBitsToBools(bytes, quantity));
        }

        [TestMethod]
        public void ConvertBitsToBoolsWhenQuantityMatchesBits()
        {
            // Arrange
            byte[] bytes =
            [
                0x0A, // 00001010
                0xAA, // 10101010
            ];
            const ushort quantity = 16;

            // Act
            var actualBools = _sut.ConvertBitsToBools(bytes, quantity);

            // Assert
            var expectedBools = new[]
                                {
                                    false, true, false, true, false, false, false, false, // 00001010 read LSB to MSB
                                    false, true, false, true, false, true, false, true, // 10101010 read LSB to MSB
                                };
            CollectionAssert.AreEqual(expectedBools, actualBools);
        }

        [TestMethod]
        public void ConvertBitsToBoolsTruncatingWhenQuantityIsLess()
        {
            // Arrange
            byte[] bytes =
            [
                0x0A, // 00001010
                0xFB, // 11111011
            ];
            const ushort quantity = 11;

            // Act
            var actualBools = _sut.ConvertBitsToBools(bytes, quantity);

            // Assert
            var expectedBools = new[]
                                {
                                    false, true, false, true, false, false, false, false, // 00001010 read LSB to MSB
                                    true, true, false, // 11111011 read LSB to MSB (only first three bits used)
                                };
            CollectionAssert.AreEqual(expectedBools, actualBools);
        }

        [TestMethod]
        public void CastShortToBytes()
        {
            // Arrange
            short[] values = [1, 2];
            byte[] expectedBytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x02, 0x00] : [0x00, 0x01, 0x00, 0x02];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastEmptyShortToBytes()
        {
            // Arrange
            short[] values = [];
            byte[] expectedBytes = [];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastIntToBytes()
        {
            // Arrange
            int[] values = [1, 2];
            byte[] expectedBytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00] : [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastEmptyIntToBytes()
        {
            // Arrange
            int[] values = [];
            byte[] expectedBytes = [];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastLongToBytes()
        {
            // Arrange
            long[] values = [1, 2];
            byte[] expectedBytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00] :
                                       [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastEmptyLongToBytes()
        {
            // Arrange
            long[] values = [];
            byte[] expectedBytes = [];

            // Act / Assert
            CastToBytes(values, expectedBytes);
        }

        [TestMethod]
        public void CastBytesToShort()
        {
            // Arrange
            byte[] bytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x02, 0x00] : [0x00, 0x01, 0x00, 0x02];
            short[] expectedValues = [1, 2];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void CastEmptyBytesToShort()
        {
            // Arrange
            byte[] bytes = [];
            short[] expectedValues = [];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void CastBytesToInt()
        {
            // Arrange
            byte[] bytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00] : [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02];
            int[] expectedValues = [1, 2];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void CastEmptyBytesToInt()
        {
            // Arrange
            byte[] bytes = [];
            int[] expectedValues = [];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void CastBytesToLong()
        {
            // Arrange
            byte[] bytes = BitConverter.IsLittleEndian ? [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00] :
                               [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02];
            long[] expectedValues = [1, 2];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void CastEmptyBytesToLong()
        {
            // Arrange
            byte[] bytes = [];
            long[] expectedValues = [];

            // Act / Assert
            CastFromBytes(bytes, expectedValues);
        }

        [TestMethod]
        public void GetBytesOfShort()
        {
            // Arrange
            var sut = new ModbusDataConverter(new BitConverterProxy()); // Use real BitConverterProxy to verify actual byte order behavior
            const short value = -2;

            // Act
            var bytes = sut.GetBytes(value);

            // Assert
            byte[] expectedBytes = BitConverter.IsLittleEndian ? [0xFE, 0xFF] : [0xFF, 0xFE];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        [TestMethod]
        public void GetBytesOfUShort()
        {
            // Arrange
            var sut = new ModbusDataConverter(new BitConverterProxy()); // Use real BitConverterProxy to verify actual byte order behavior
            const ushort value = 258;

            // Act
            var bytes = sut.GetBytes(value);

            // Assert
            byte[] expectedBytes = BitConverter.IsLittleEndian ? [0x02, 0x01] : [0x01, 0x02];
            CollectionAssert.AreEqual(expectedBytes, bytes);
        }

        private void CastToBytes<T>(T[] values, byte[] expectedBytes)
            where T : unmanaged
        {
            // Act
            var actualBytes = _sut.CastToBytes(values);

            // Assert
            CollectionAssert.AreEqual(expectedBytes, actualBytes);
        }

        private void CastFromBytes<T>(byte[] bytes, T[] expectedValues)
            where T : unmanaged
        {
            // Act
            var actualValues = _sut.CastFromBytes<T>(bytes);

            // Assert
            CollectionAssert.AreEqual(expectedValues, actualValues);
        }
    }
}