using System;
using System.Collections.Generic;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Rtu.Test
{
    [TestClass]
    public class ModbusRtuShould
    {
        private const int UnitIdentifier = 42;

        private const ushort StartingAddress = 10;

        private const ushort Quantity = 12;

        private const uint Count = 3;

        private const int BytesPer16BitValue = 2;

        private const int BytesPer32BitValue = 4;

        private const int BytesPer64BitValue = 8;

        private const ByteOrder ByteOrder = Vion.Dale.Sdk.Modbus.Core.Conversion.ByteOrder.LsbToMsb;

        private const WordOrder32 WordOrder32 = Vion.Dale.Sdk.Modbus.Core.Conversion.WordOrder32.LswToMsw;

        private const WordOrder64 WordOrder64 = Vion.Dale.Sdk.Modbus.Core.Conversion.WordOrder64.BADC;

        private const string ContractIdentifier = "rtu0";

        private const string LogicBlockIdValue = "lb0";

        private static readonly byte[] RegisterBytes = [0x22, 0xB2, 0xC3, 0xB4];

        private static readonly DateTime RequestTime = new(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           DateTimeKind.Utc);

        private static readonly ReadModbusRtuRequest ReadRequestStub = new(ModbusFunctionCode.None,
                                                                           0,
                                                                           0,
                                                                           0,
                                                                           RequestTime,
                                                                           RequestTime,
                                                                           Guid.NewGuid(),
                                                                           (_, _) => { });

        private static readonly WriteModbusRtuRequest WriteRequestStub = new(ModbusFunctionCode.None,
                                                                             0,
                                                                             0,
                                                                             [],
                                                                             RequestTime,
                                                                             RequestTime,
                                                                             Guid.NewGuid(),
                                                                             _ => { });

        public enum TargetMethod
        {
            ReadDiscreteInputs,

            ReadCoils,

            WriteSingleCoil,

            WriteMultipleCoils,

            ReadInputRegistersAsFloat,

            ReadHoldingRegistersAsInt,

            WriteMultipleHoldingRegistersAsDouble,
        }

        private readonly Mock<IActorContext> _actorContextMock = new();

        private readonly Mock<IModbusDataConverter> _dataConverterMock = new();

        private readonly Mock<IActorReference> _handlerRefMock = new();

        private readonly Mock<ILogger<ModbusRtu>> _loggerMock = new();

        private readonly Mock<IModbusRtuRequestFactory> _requestFactoryMock = new();

        private readonly Mock<IModbusValidator> _validatorMock = new();

        private Func<Memory<byte>, bool[]>? _capturedBoolArrayProcessResponse;

        private Func<Memory<byte>, byte[]>? _capturedByteArrayProcessResponse;

        private Func<Memory<byte>, double[]>? _capturedDoubleArrayProcessResponse;

        private Func<Memory<byte>, float[]>? _capturedFloatArrayProcessResponse;

        private Func<Memory<byte>, int[]>? _capturedIntArrayProcessResponse;

        private Func<Memory<byte>, long[]>? _capturedLongArrayProcessResponse;

        private Func<Memory<byte>, short[]>? _capturedShortArrayProcessResponse;

        private Func<Memory<byte>, string>? _capturedStringProcessResponse;

        private Func<Memory<byte>, uint[]>? _capturedUIntArrayProcessResponse;

        private Func<Memory<byte>, ulong[]>? _capturedULongArrayProcessResponse;

        private Func<Memory<byte>, ushort[]>? _capturedUShortArrayProcessResponse;

        private ModbusRtu _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new ModbusRtu(ContractIdentifier,
                                 _actorContextMock.Object,
                                 _requestFactoryMock.Object,
                                 _dataConverterMock.Object,
                                 _validatorMock.Object,
                                 _loggerMock.Object);
            _sut.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId(LogicBlockIdValue), ContractIdentifier));
            _sut.SetLinkedContractHandler(_handlerRefMock.Object);
            _sut.IsEnabled = true;

            SetupReadArrayCapture<bool>(processResponse => _capturedBoolArrayProcessResponse = processResponse);
            SetupReadArrayCapture<byte>(processResponse => _capturedByteArrayProcessResponse = processResponse);
            SetupReadArrayCapture<short>(processResponse => _capturedShortArrayProcessResponse = processResponse);
            SetupReadArrayCapture<ushort>(processResponse => _capturedUShortArrayProcessResponse = processResponse);
            SetupReadArrayCapture<int>(processResponse => _capturedIntArrayProcessResponse = processResponse);
            SetupReadArrayCapture<uint>(processResponse => _capturedUIntArrayProcessResponse = processResponse);
            SetupReadArrayCapture<float>(processResponse => _capturedFloatArrayProcessResponse = processResponse);
            SetupReadArrayCapture<long>(processResponse => _capturedLongArrayProcessResponse = processResponse);
            SetupReadArrayCapture<ulong>(processResponse => _capturedULongArrayProcessResponse = processResponse);
            SetupReadArrayCapture<double>(processResponse => _capturedDoubleArrayProcessResponse = processResponse);
            SetupReadSingleCapture<string>(processResponse => _capturedStringProcessResponse = processResponse);
            SetupWriteCapture();

            _dataConverterMock.Setup(converter => converter.ConvertCountToQuantity(It.IsAny<uint>(), It.IsAny<int>())).Returns(Quantity);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<short[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<ushort[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<int[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<uint[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<float[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<long[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<ulong[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<double[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.CastToBytes(It.IsAny<bool[]>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.GetBytes(It.IsAny<short>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.GetBytes(It.IsAny<ushort>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.ConvertStringToBytes(It.IsAny<string>(), It.IsAny<TextEncoding>())).Returns(RegisterBytes);
            _dataConverterMock.Setup(converter => converter.ToByte(It.IsAny<bool>())).Returns(1);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputs)]
        [DataRow(TargetMethod.ReadCoils)]
        [DataRow(TargetMethod.WriteSingleCoil)]
        [DataRow(TargetMethod.WriteMultipleCoils)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloat)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsInt)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDouble)]
        public void SkipRequestWhenDisabled(TargetMethod targetMethod)
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            InvokeMethod(targetMethod);

            // Assert
            _validatorMock.VerifyNoOtherCalls();
            _requestFactoryMock.VerifyNoOtherCalls();
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputs)]
        [DataRow(TargetMethod.ReadCoils)]
        [DataRow(TargetMethod.WriteSingleCoil)]
        [DataRow(TargetMethod.WriteMultipleCoils)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloat)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsInt)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDouble)]
        public void ValidateUnitIdentifier(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            InvokeMethod(targetMethod);

            // Assert
            _validatorMock.Verify(validator => validator.ValidateUnitIdentifier(UnitIdentifier), Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputs)]
        [DataRow(TargetMethod.ReadCoils)]
        [DataRow(TargetMethod.WriteSingleCoil)]
        [DataRow(TargetMethod.WriteMultipleCoils)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloat)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsInt)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDouble)]
        public void InvokeErrorCallbackWhenValidatorThrows(TargetMethod targetMethod)
        {
            // Arrange
            var expectedException = new InvalidUnitIdentifierException(1);
            _validatorMock.Setup(validator => validator.ValidateUnitIdentifier(It.IsAny<int>())).Throws(expectedException);
            Exception? capturedException = null;

            // Act
            InvokeMethod(targetMethod, exception => capturedException = exception);

            // Assert
            Assert.AreSame(expectedException, capturedException);
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputs)]
        [DataRow(TargetMethod.ReadCoils)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloat)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsInt)]
        public void SendReadRequestToContractHandler(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            InvokeMethod(targetMethod);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_handlerRefMock.Object,
                                                                         It.Is<object>(message => message is ContractMessage<ReadModbusRtuRequest>),
                                                                         It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.WriteSingleCoil)]
        [DataRow(TargetMethod.WriteMultipleCoils)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDouble)]
        public void SendWriteRequestToContractHandler(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            InvokeMethod(targetMethod);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_handlerRefMock.Object,
                                                                         It.Is<object>(message => message is ContractMessage<WriteModbusRtuRequest>),
                                                                         It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        public void DropMessageWhenLogicBlockIdIsEmpty()
        {
            // Arrange
            _sut.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId(string.Empty), ContractIdentifier));

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier, StartingAddress, Quantity, _ => { });

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public void InvokeReadResponseCallback()
        {
            // Arrange
            byte[] responseData = [0xAA, 0xBB];
            var responseException = new Exception();
            byte[]? capturedData = null;
            Exception? capturedException = null;
            var response = new ReadModbusRtuResponse(responseData,
                                                     responseException,
                                                     (data, exception) =>
                                                     {
                                                         capturedData = data;
                                                         capturedException = exception;
                                                     },
                                                     Guid.NewGuid());

            // Act
            _sut.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(default, response));

            // Assert
            Assert.AreSame(responseData, capturedData);
            Assert.AreSame(responseException, capturedException);
        }

        [TestMethod]
        public void InvokeWriteResponseCallback()
        {
            // Arrange
            var responseException = new Exception();
            Exception? capturedException = null;
            var response = new WriteModbusRtuResponse(responseException, exception => capturedException = exception, Guid.NewGuid());

            // Act
            _sut.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(default, response));

            // Assert
            Assert.AreSame(responseException, capturedException);
        }

        [TestMethod]
        public void ReadDiscreteInputs()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            _dataConverterMock.Setup(converter => converter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), Quantity)).Returns(expectedBools);

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier, StartingAddress, Quantity, _ => { });

            // Assert
            VerifyCreateReadArrayInvoked<bool>(ModbusFunctionCode.ReadDiscreteInputs, Quantity);
            var result = _capturedBoolArrayProcessResponse!(RegisterBytes);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, result);
        }

        [TestMethod]
        public void ReadCoils()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            _dataConverterMock.Setup(converter => converter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), Quantity)).Returns(expectedBools);

            // Act
            _sut.ReadCoils(UnitIdentifier, StartingAddress, Quantity, _ => { });

            // Assert
            VerifyCreateReadArrayInvoked<bool>(ModbusFunctionCode.ReadCoils, Quantity);
            var result = _capturedBoolArrayProcessResponse!(RegisterBytes);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, result);
        }

        [TestMethod]
        public void WriteSingleCoil()
        {
            // Arrange
            const bool value = true;

            // Act
            _sut.WriteSingleCoil(UnitIdentifier, StartingAddress, value);

            // Assert
            _dataConverterMock.Verify(converter => converter.ToByte(value), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleCoil, [_dataConverterMock.Object.ToByte(value)]);
        }

        [TestMethod]
        public void WriteMultipleCoils()
        {
            // Arrange
            bool[] values = [true, false, true, true];

            // Act
            _sut.WriteMultipleCoils(UnitIdentifier, StartingAddress, values);

            // Assert
            _dataConverterMock.Verify(converter => converter.CastToBytes(values), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleCoils, RegisterBytes);
        }

        [TestMethod]
        public void ReadInputRegistersRaw()
        {
            // Arrange

            // Act
            _sut.ReadInputRegistersRaw(UnitIdentifier, StartingAddress, Quantity, _ => { });

            // Assert
            VerifyCreateReadArrayInvoked<byte>(ModbusFunctionCode.ReadInputRegisters, Quantity);
            var result = _capturedByteArrayProcessResponse!(RegisterBytes);
            CollectionAssert.AreEqual(RegisterBytes, result);
        }

        [TestMethod]
        public void ReadInputRegistersAsShort()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsShort(UnitIdentifier, StartingAddress, Quantity, _ => { }, byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            new short[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedShortArrayProcessResponse!);
        }

        [TestMethod]
        public void ReadInputRegistersAsUShort()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsUShort(UnitIdentifier, StartingAddress, Quantity, _ => { }, byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            new ushort[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedUShortArrayProcessResponse!);
        }

        [TestMethod]
        public void ReadInputRegistersAsInt()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsInt(UnitIdentifier,
                                                               StartingAddress,
                                                               Count,
                                                               _ => { },
                                                               byteOrder: ByteOrder,
                                                               wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1, 2],
                            BytesPer32BitValue,
                            () => _capturedIntArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadInputRegistersAsUInt()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsUInt(UnitIdentifier,
                                                                StartingAddress,
                                                                Count,
                                                                _ => { },
                                                                byteOrder: ByteOrder,
                                                                wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1U, 2U],
                            BytesPer32BitValue,
                            () => _capturedUIntArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadInputRegistersAsFloat()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _ => { },
                                                                 byteOrder: ByteOrder,
                                                                 wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1f, 2f],
                            BytesPer32BitValue,
                            () => _capturedFloatArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadInputRegistersAsLong()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsLong(UnitIdentifier,
                                                                StartingAddress,
                                                                Count,
                                                                _ => { },
                                                                byteOrder: ByteOrder,
                                                                wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1L, 2L],
                            BytesPer64BitValue,
                            () => _capturedLongArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        public void ReadInputRegistersAsULong()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsULong(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _ => { },
                                                                 byteOrder: ByteOrder,
                                                                 wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1UL, 2UL],
                            BytesPer64BitValue,
                            () => _capturedULongArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        public void ReadInputRegistersAsDouble()
        {
            ReadRegistersAs(() => _sut.ReadInputRegistersAsDouble(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _ => { },
                                                                  byteOrder: ByteOrder,
                                                                  wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            [1d, 2d],
                            BytesPer64BitValue,
                            () => _capturedDoubleArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public void ReadInputRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            var expectedString = Guid.NewGuid().ToString();
            _dataConverterMock.Setup(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), It.IsAny<TextEncoding>())).Returns(expectedString);

            // Act
            _sut.ReadInputRegistersAsString(UnitIdentifier, StartingAddress, Quantity, _ => { }, textEncoding: textEncoding);

            // Assert
            VerifyCreateReadSingleInvoked<string>(ModbusFunctionCode.ReadInputRegisters, Quantity);
            var result = _capturedStringProcessResponse!(RegisterBytes);
            _dataConverterMock.Verify(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), textEncoding), Times.Once);
            Assert.AreEqual(expectedString, result);
        }

        [TestMethod]
        public void ReadHoldingRegistersRaw()
        {
            // Arrange

            // Act
            _sut.ReadHoldingRegistersRaw(UnitIdentifier, StartingAddress, Quantity, _ => { });

            // Assert
            VerifyCreateReadArrayInvoked<byte>(ModbusFunctionCode.ReadHoldingRegisters, Quantity);
            var result = _capturedByteArrayProcessResponse!(RegisterBytes);
            CollectionAssert.AreEqual(RegisterBytes, result);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsShort()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsShort(UnitIdentifier, StartingAddress, Quantity, _ => { }, byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            new short[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedShortArrayProcessResponse!);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsUShort()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsUShort(UnitIdentifier, StartingAddress, Quantity, _ => { }, byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            new ushort[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedUShortArrayProcessResponse!);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsInt()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _ => { },
                                                                 byteOrder: ByteOrder,
                                                                 wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1, 2],
                            BytesPer32BitValue,
                            () => _capturedIntArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsUInt()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsUInt(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _ => { },
                                                                  byteOrder: ByteOrder,
                                                                  wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1U, 2U],
                            BytesPer32BitValue,
                            () => _capturedUIntArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsFloat()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsFloat(UnitIdentifier,
                                                                   StartingAddress,
                                                                   Count,
                                                                   _ => { },
                                                                   byteOrder: ByteOrder,
                                                                   wordOrder: WordOrder32),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1f, 2f],
                            BytesPer32BitValue,
                            () => _capturedFloatArrayProcessResponse!,
                            VerifySwapWords32Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsLong()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsLong(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _ => { },
                                                                  byteOrder: ByteOrder,
                                                                  wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1L, 2L],
                            BytesPer64BitValue,
                            () => _capturedLongArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsULong()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsULong(UnitIdentifier,
                                                                   StartingAddress,
                                                                   Count,
                                                                   _ => { },
                                                                   byteOrder: ByteOrder,
                                                                   wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1UL, 2UL],
                            BytesPer64BitValue,
                            () => _capturedULongArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        public void ReadHoldingRegistersAsDouble()
        {
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsDouble(UnitIdentifier,
                                                                    StartingAddress,
                                                                    Count,
                                                                    _ => { },
                                                                    byteOrder: ByteOrder,
                                                                    wordOrder: WordOrder64),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            [1d, 2d],
                            BytesPer64BitValue,
                            () => _capturedDoubleArrayProcessResponse!,
                            VerifySwapWords64Invoked);
            VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public void ReadHoldingRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            var expectedString = Guid.NewGuid().ToString();
            _dataConverterMock.Setup(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), It.IsAny<TextEncoding>())).Returns(expectedString);

            // Act
            _sut.ReadHoldingRegistersAsString(UnitIdentifier, StartingAddress, Quantity, _ => { }, textEncoding: textEncoding);

            // Assert
            VerifyCreateReadSingleInvoked<string>(ModbusFunctionCode.ReadHoldingRegisters, Quantity);
            var result = _capturedStringProcessResponse!(RegisterBytes);
            _dataConverterMock.Verify(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), textEncoding), Times.Once);
            Assert.AreEqual(expectedString, result);
        }

        [TestMethod]
        public void WriteSingleHoldingRegisterAsShort()
        {
            // Arrange
            const short value = 42;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier, StartingAddress, value, byteOrder: ByteOrder);

            // Assert
            _dataConverterMock.Verify(converter => converter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleRegister, RegisterBytes);
        }

        [TestMethod]
        public void WriteSingleHoldingRegisterAsUShort()
        {
            // Arrange
            const ushort value = 42;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier, StartingAddress, value, byteOrder: ByteOrder);

            // Assert
            _dataConverterMock.Verify(converter => converter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleRegister, RegisterBytes);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersRaw()
        {
            // Arrange

            // Act
            _sut.WriteMultipleHoldingRegistersRaw(UnitIdentifier, StartingAddress, RegisterBytes);

            // Assert
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleRegisters, RegisterBytes);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsShort()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsShort(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder), new short[] { 1, 2 });
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsUShort()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsUShort(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder), new ushort[] { 1, 2 });
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsInt()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsInt(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder32),
                                    [1, 2],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsUInt()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsUInt(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder32),
                                    [1U, 2U],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsFloat()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsFloat(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder32),
                                    [1.1f, 2.2f],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsLong()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsLong(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder64),
                                    [1L, 2L],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsULong()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsULong(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder64),
                                    [1UL, 2UL],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        public void WriteMultipleHoldingRegistersAsDouble()
        {
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier, StartingAddress, values, byteOrder: ByteOrder, wordOrder: WordOrder64),
                                    [1.1, 2.2],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public void WriteMultipleHoldingRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            const string value = "test";

            // Act
            _sut.WriteMultipleHoldingRegistersAsString(UnitIdentifier, StartingAddress, value, textEncoding: textEncoding);

            // Assert
            _dataConverterMock.Verify(converter => converter.ConvertStringToBytes(value, textEncoding), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleRegisters, RegisterBytes);
        }

        private void InvokeMethod(TargetMethod targetMethod, Action<Exception>? errorCallback = null)
        {
            switch (targetMethod)
            {
                case TargetMethod.ReadDiscreteInputs:
                    _sut.ReadDiscreteInputs(UnitIdentifier, StartingAddress, Quantity, _ => { }, errorCallback);
                    break;
                case TargetMethod.ReadCoils:
                    _sut.ReadCoils(UnitIdentifier, StartingAddress, Quantity, _ => { }, errorCallback);
                    break;
                case TargetMethod.WriteSingleCoil:
                    _sut.WriteSingleCoil(UnitIdentifier, StartingAddress, true, errorCallback: errorCallback);
                    break;
                case TargetMethod.WriteMultipleCoils:
                    _sut.WriteMultipleCoils(UnitIdentifier, StartingAddress, [true, false], errorCallback: errorCallback);
                    break;
                case TargetMethod.ReadInputRegistersAsFloat:
                    _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                                   StartingAddress,
                                                   Count,
                                                   _ => { },
                                                   errorCallback,
                                                   ByteOrder,
                                                   WordOrder32);
                    break;
                case TargetMethod.ReadHoldingRegistersAsInt:
                    _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                                   StartingAddress,
                                                   Count,
                                                   _ => { },
                                                   errorCallback,
                                                   ByteOrder,
                                                   WordOrder32);
                    break;
                case TargetMethod.WriteMultipleHoldingRegistersAsDouble:
                    _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier,
                                                               StartingAddress,
                                                               [1.1, 2.2, 3.3],
                                                               errorCallback: errorCallback,
                                                               byteOrder: ByteOrder,
                                                               wordOrder: WordOrder64);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(targetMethod), targetMethod, null);
            }
        }

        private void ReadRegistersAs<T>(Action invokeSut,
                                        ModbusFunctionCode expectedFunctionCode,
                                        ushort expectedQuantity,
                                        T[] expectedValues,
                                        int bytesPerValue,
                                        Func<Func<Memory<byte>, T[]>> getCapturedProcessResponse,
                                        Action? additionalVerifications = null)
            where T : unmanaged
        {
            // Arrange
            _dataConverterMock.Setup(converter => converter.CastFromBytes<T>(It.IsAny<Memory<byte>>())).Returns(expectedValues);

            // Act
            invokeSut();

            // Assert
            VerifyCreateReadArrayInvoked<T>(expectedFunctionCode, expectedQuantity);
            var result = getCapturedProcessResponse()(RegisterBytes);
            _validatorMock.Verify(validator => validator.ValidateResponseAlignment(RegisterBytes.Length, bytesPerValue, UnitIdentifier, StartingAddress), Times.Once);
            VerifySwapBytesInvoked();
            additionalVerifications?.Invoke();
            _dataConverterMock.Verify(converter => converter.CastFromBytes<T>(It.IsAny<Memory<byte>>()), Times.Once);
            CollectionAssert.AreEqual(expectedValues, result);
        }

        private void WriteHoldingRegistersAs<T>(Action<T[]> invokeSut, T[] values, Action? additionalVerifications = null)
            where T : unmanaged
        {
            // Arrange

            // Act
            invokeSut(values);

            // Assert
            _dataConverterMock.Verify(converter => converter.CastToBytes(values), Times.Once);
            VerifySwapBytesInvoked();
            additionalVerifications?.Invoke();
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleRegisters, RegisterBytes);
        }

        private void SetupReadArrayCapture<T>(Action<Func<Memory<byte>, T[]>> capture)
        {
            _requestFactoryMock.Setup(factory => factory.CreateReadRequest(It.IsAny<ModbusFunctionCode>(),
                                                                           It.IsAny<int>(),
                                                                           It.IsAny<ushort>(),
                                                                           It.IsAny<ushort>(),
                                                                           It.IsAny<TimeSpan>(),
                                                                           It.IsAny<Func<Memory<byte>, T[]>>(),
                                                                           It.IsAny<Action<T[]>>(),
                                                                           It.IsAny<Action<Exception>?>()))
                               .Callback<ModbusFunctionCode, int, ushort, ushort, TimeSpan, Func<Memory<byte>, T[]>, Action<T[]>, Action<Exception>?>((_,
                                   _,
                                   _,
                                   _,
                                   _,
                                   processResponse,
                                   _,
                                   _) => capture(processResponse))
                               .Returns(ReadRequestStub);
        }

        private void SetupReadSingleCapture<T>(Action<Func<Memory<byte>, T>> capture)
        {
            _requestFactoryMock.Setup(factory => factory.CreateReadRequest(It.IsAny<ModbusFunctionCode>(),
                                                                           It.IsAny<int>(),
                                                                           It.IsAny<ushort>(),
                                                                           It.IsAny<ushort>(),
                                                                           It.IsAny<TimeSpan>(),
                                                                           It.IsAny<Func<Memory<byte>, T>>(),
                                                                           It.IsAny<Action<T>>(),
                                                                           It.IsAny<Action<Exception>?>()))
                               .Callback<ModbusFunctionCode, int, ushort, ushort, TimeSpan, Func<Memory<byte>, T>, Action<T>, Action<Exception>?>((_,
                                   _,
                                   _,
                                   _,
                                   _,
                                   processResponse,
                                   _,
                                   _) => capture(processResponse))
                               .Returns(ReadRequestStub);
        }

        private void SetupWriteCapture()
        {
            _requestFactoryMock
                .Setup(factory => factory.CreateWriteRequest(It.IsAny<ModbusFunctionCode>(),
                                                             It.IsAny<int>(),
                                                             It.IsAny<ushort>(),
                                                             It.IsAny<byte[]>(),
                                                             It.IsAny<TimeSpan>(),
                                                             It.IsAny<Action?>(),
                                                             It.IsAny<Action<Exception>?>()))
                .Callback<ModbusFunctionCode, int, ushort, byte[], TimeSpan, Action?, Action<Exception>?>((_,
                                                                                                           _,
                                                                                                           _,
                                                                                                           data,
                                                                                                           _,
                                                                                                           _,
                                                                                                           _) =>
                                                                                                          {
                                                                                                          })
                .Returns(WriteRequestStub);
        }

        private void VerifyCreateReadArrayInvoked<T>(ModbusFunctionCode expectedFunctionCode, ushort expectedQuantity)
        {
            _requestFactoryMock.Verify(factory => factory.CreateReadRequest(expectedFunctionCode,
                                                                            UnitIdentifier,
                                                                            StartingAddress,
                                                                            expectedQuantity,
                                                                            It.IsAny<TimeSpan>(),
                                                                            It.IsAny<Func<Memory<byte>, T[]>>(),
                                                                            It.IsAny<Action<T[]>>(),
                                                                            It.IsAny<Action<Exception>?>()),
                                       Times.Once);
        }

        private void VerifyCreateReadSingleInvoked<T>(ModbusFunctionCode expectedFunctionCode, ushort expectedQuantity)
        {
            _requestFactoryMock.Verify(factory => factory.CreateReadRequest(expectedFunctionCode,
                                                                            UnitIdentifier,
                                                                            StartingAddress,
                                                                            expectedQuantity,
                                                                            It.IsAny<TimeSpan>(),
                                                                            It.IsAny<Func<Memory<byte>, T>>(),
                                                                            It.IsAny<Action<T>>(),
                                                                            It.IsAny<Action<Exception>?>()),
                                       Times.Once);
        }

        private void VerifyCreateWriteInvoked(ModbusFunctionCode expectedFunctionCode, byte[] expectedData)
        {
            _requestFactoryMock.Verify(factory => factory.CreateWriteRequest(expectedFunctionCode,
                                                                             UnitIdentifier,
                                                                             StartingAddress,
                                                                             expectedData,
                                                                             It.IsAny<TimeSpan>(),
                                                                             It.IsAny<Action?>(),
                                                                             It.IsAny<Action<Exception>?>()),
                                       Times.Once);
        }

        private void VerifyConvertBitsToBoolsInvoked()
        {
            _dataConverterMock.Verify(converter => converter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), Quantity), Times.Once);
        }

        private void VerifyConvertCountToQuantityInvoked(int bytesPerValue)
        {
            _dataConverterMock.Verify(converter => converter.ConvertCountToQuantity(Count, bytesPerValue), Times.Once);
        }

        private void VerifySwapBytesInvoked()
        {
            _dataConverterMock.Verify(converter => converter.SwapBytes(It.IsAny<Memory<byte>>(), ByteOrder), Times.Once);
        }

        private void VerifySwapWords32Invoked()
        {
            _dataConverterMock.Verify(converter => converter.SwapWords(It.IsAny<Memory<byte>>(), WordOrder32), Times.Once);
        }

        private void VerifySwapWords64Invoked()
        {
            _dataConverterMock.Verify(converter => converter.SwapWords(It.IsAny<Memory<byte>>(), WordOrder64), Times.Once);
        }
    }
}