using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Utils;

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

        private const ByteOrder ByteOrder = Core.Conversion.ByteOrder.LsbToMsb;

        private const WordOrder32 WordOrder32 = Core.Conversion.WordOrder32.LswToMsw;

        private const WordOrder64 WordOrder64 = Core.Conversion.WordOrder64.BADC;

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
                                                                           TimeSpan.FromSeconds(5),
                                                                           null,
                                                                           Guid.NewGuid(),
                                                                           (_, _, _) => { });

        private static readonly WriteModbusRtuRequest WriteRequestStub = new(ModbusFunctionCode.None,
                                                                             0,
                                                                             0,
                                                                             [],
                                                                             RequestTime,
                                                                             TimeSpan.FromSeconds(5),
                                                                             null,
                                                                             Guid.NewGuid(),
                                                                             (_, _) => { });

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

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<IActorReference> _handlerRefMock = new();

        private readonly Mock<ILogger<ModbusRtu>> _loggerMock = new();

        private readonly List<Action> _pendingDispatcherActions = [];

        private readonly Mock<IModbusRtuRequestFactory> _requestFactoryMock = new();

        private readonly FakeTimeProvider _timeProvider = new(RequestTime);

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
                                 _timeProvider,
                                 _loggerMock.Object);
            _sut.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId(LogicBlockIdValue), ContractIdentifier));
            _sut.SetLinkedContractHandler(_handlerRefMock.Object);
            _sut.IsEnabled = true;

            // Errors reach the caller through its dispatcher, never inside the call. Draining the dispatcher
            // inline keeps these tests about what the contract does rather than about the actor hop.
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _pendingDispatcherActions.Add(action));

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
        [TestProperty("spec", "AC-MODB-003.4")]
        [DataRow(0, DisplayName = "Zero")]
        [DataRow(-1, DisplayName = "Negative")]
        public void ThrowExceptionWhenDefaultOperationTimeoutNotPositive(int seconds)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.DefaultOperationTimeout = TimeSpan.FromSeconds(seconds));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.4")]
        public void ThrowExceptionWhenDefaultOperationTimeoutAboveFrameworkCeiling()
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.DefaultOperationTimeout = ModbusTimeoutLimits.MaxTimeout + TimeSpan.FromMilliseconds(1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.4")]
        public void ThrowExceptionWhenPerCallOperationTimeoutAboveFrameworkCeiling()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.WriteMultipleCoils(UnitIdentifier,
                                                                                            StartingAddress,
                                                                                            [true],
                                                                                            _dispatcherMock.Object,
                                                                                            operationTimeout: ModbusTimeoutLimits.MaxTimeout + TimeSpan.FromMilliseconds(1)));
            _requestFactoryMock.VerifyNoOtherCalls();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-004.4")]
        [DataRow(TargetMethod.ReadDiscreteInputs, ModbusProtocolLimits.MaxBitsPerRead)]
        [DataRow(TargetMethod.ReadCoils, ModbusProtocolLimits.MaxBitsPerRead)]
        [DataRow(TargetMethod.WriteSingleCoil, ModbusProtocolLimits.MaxBitsPerWrite)]
        [DataRow(TargetMethod.WriteMultipleCoils, ModbusProtocolLimits.MaxBitsPerWrite)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloat, ModbusProtocolLimits.MaxRegistersPerRead)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsInt, ModbusProtocolLimits.MaxRegistersPerRead)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDouble, ModbusProtocolLimits.MaxRegistersPerWrite)]
        public void ValidateQuantityAgainstFunctionCodesProtocolLimit(TargetMethod targetMethod, int expectedLimit)
        {
            // Arrange

            // Act
            InvokeMethod(targetMethod);

            // Assert
            _validatorMock.Verify(validator => validator.ValidateQuantity(It.IsAny<uint>(), expectedLimit), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
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
        [TestProperty("spec", "AC-MODB-004.1")]
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
        [TestProperty("spec", "AC-MODB-004.1")]
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
            InvokeMethod(targetMethod, (exception, _) => capturedException = exception);

            // Assert — the failure is delivered through the dispatcher, not inside the call.
            Assert.IsNull(capturedException);
            DrainDispatcher();
            Assert.AreSame(expectedException, capturedException);
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.4")]
        public void RecordValidationFailureAsInvalidInLinkSummary()
        {
            // Arrange
            _validatorMock.Setup(validator => validator.ValidateUnitIdentifier(It.IsAny<int>())).Throws(new InvalidUnitIdentifierException(1));
            ModbusReceipt? capturedReceipt = null;

            // Act
            InvokeMethod(TargetMethod.ReadCoils, (_, receipt) => capturedReceipt = receipt);
            DrainDispatcher();

            // Assert
            Assert.AreEqual(ModbusOutcome.Invalid, capturedReceipt!.Value.Outcome);
            Assert.AreEqual(ModbusOutcome.Invalid, _sut.Link.LastFailureOutcome);

            // A caller error says nothing about the device, so the link verdict must not move.
            Assert.AreEqual(ModbusLinkState.Unknown, _sut.Link.State);
            Assert.AreEqual(0, _sut.Link.SuccessCount);
            Assert.AreEqual(0, _sut.Link.TimeoutCount);
            Assert.AreEqual(0, _sut.Link.TransportErrorCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.1")]
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
        [TestProperty("spec", "AC-MODB-015.1")]
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
        [TestProperty("spec", "AC-BIND-013.1")]
        public void DropMessageWhenLogicBlockIdEmpty()
        {
            // Arrange
            _sut.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId(string.Empty), ContractIdentifier));

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, (_, _) => { });

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeReadResponseCallback()
        {
            // Arrange
            byte[] responseData = [0xAA, 0xBB];
            var responseException = new Exception();
            byte[]? capturedData = null;
            Exception? capturedException = null;
            ModbusReceipt? capturedReceipt = null;
            var response = new ReadModbusRtuResponse(responseData,
                                                     responseException,
                                                     (data, exception, receipt) =>
                                                     {
                                                         capturedData = data;
                                                         capturedException = exception;
                                                         capturedReceipt = receipt;
                                                     },
                                                     Guid.NewGuid(),
                                                     RequestTime,
                                                     RequestTime.AddMilliseconds(5),
                                                     RequestTime.AddMilliseconds(25),
                                                     250,
                                                     ModbusOutcome.Timeout);

            // Act
            _sut.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(default, response));

            // Assert
            Assert.AreSame(responseData, capturedData);
            Assert.AreSame(responseException, capturedException);
            Assert.AreEqual(ModbusOutcome.Timeout, capturedReceipt!.Value.Outcome);
            Assert.AreEqual(TimeSpan.FromMilliseconds(5), capturedReceipt.Value.QueuedWait);
            Assert.AreEqual(TimeSpan.FromMilliseconds(20), capturedReceipt.Value.RoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeWriteResponseCallback()
        {
            // Arrange
            var responseException = new Exception();
            Exception? capturedException = null;
            var response = new WriteModbusRtuResponse(responseException,
                                                      (exception, _) => capturedException = exception,
                                                      Guid.NewGuid(),
                                                      RequestTime,
                                                      RequestTime.AddMilliseconds(5),
                                                      RequestTime.AddMilliseconds(25),
                                                      250,
                                                      ModbusOutcome.DeviceError);

            // Act
            _sut.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(default, response));

            // Assert
            Assert.AreSame(responseException, capturedException);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadDiscreteInputs()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            _dataConverterMock.Setup(converter => converter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), Quantity)).Returns(expectedBools);

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, (_, _) => { });

            // Assert
            VerifyCreateReadArrayInvoked<bool>(ModbusFunctionCode.ReadDiscreteInputs, Quantity);
            var result = _capturedBoolArrayProcessResponse!(RegisterBytes);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadCoils()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            _dataConverterMock.Setup(converter => converter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), Quantity)).Returns(expectedBools);

            // Act
            _sut.ReadCoils(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, (_, _) => { });

            // Assert
            VerifyCreateReadArrayInvoked<bool>(ModbusFunctionCode.ReadCoils, Quantity);
            var result = _capturedBoolArrayProcessResponse!(RegisterBytes);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteSingleCoil()
        {
            // Arrange
            const bool value = true;

            // Act
            _sut.WriteSingleCoil(UnitIdentifier, StartingAddress, value, _dispatcherMock.Object);

            // Assert
            _dataConverterMock.Verify(converter => converter.ToByte(value), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleCoil, [_dataConverterMock.Object.ToByte(value)]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleCoils()
        {
            // Arrange
            bool[] values = [true, false, true, true];

            // Act
            _sut.WriteMultipleCoils(UnitIdentifier, StartingAddress, values, _dispatcherMock.Object);

            // Assert
            _dataConverterMock.Verify(converter => converter.CastToBytes(values), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleCoils, RegisterBytes);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersRaw()
        {
            // Arrange

            // Act
            _sut.ReadInputRegistersRaw(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, (_, _) => { });

            // Assert
            VerifyCreateReadArrayInvoked<byte>(ModbusFunctionCode.ReadInputRegisters, Quantity);
            var result = _capturedByteArrayProcessResponse!(RegisterBytes);
            CollectionAssert.AreEqual(RegisterBytes, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsShort()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsShort(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Quantity,
                                                                 _dispatcherMock.Object,
                                                                 (_, _) => { },
                                                                 byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            new short[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedShortArrayProcessResponse!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsUShort()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsUShort(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Quantity,
                                                                  _dispatcherMock.Object,
                                                                  (_, _) => { },
                                                                  byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadInputRegisters,
                            Quantity,
                            new ushort[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedUShortArrayProcessResponse!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsInt()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsInt(UnitIdentifier,
                                                               StartingAddress,
                                                               Count,
                                                               _dispatcherMock.Object,
                                                               (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsUInt()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsUInt(UnitIdentifier,
                                                                StartingAddress,
                                                                Count,
                                                                _dispatcherMock.Object,
                                                                (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsFloat()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _dispatcherMock.Object,
                                                                 (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsLong()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsLong(UnitIdentifier,
                                                                StartingAddress,
                                                                Count,
                                                                _dispatcherMock.Object,
                                                                (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsULong()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsULong(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _dispatcherMock.Object,
                                                                 (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadInputRegistersAsDouble()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadInputRegistersAsDouble(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _dispatcherMock.Object,
                                                                  (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
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
            _sut.ReadInputRegistersAsString(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            (_, _) => { },
                                            textEncoding: textEncoding);

            // Assert
            VerifyCreateReadSingleInvoked<string>(ModbusFunctionCode.ReadInputRegisters, Quantity);
            var result = _capturedStringProcessResponse!(RegisterBytes);
            _dataConverterMock.Verify(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), textEncoding), Times.Once);
            Assert.AreEqual(expectedString, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersRaw()
        {
            // Arrange

            // Act
            _sut.ReadHoldingRegistersRaw(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, (_, _) => { });

            // Assert
            VerifyCreateReadArrayInvoked<byte>(ModbusFunctionCode.ReadHoldingRegisters, Quantity);
            var result = _capturedByteArrayProcessResponse!(RegisterBytes);
            CollectionAssert.AreEqual(RegisterBytes, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsShort()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsShort(UnitIdentifier,
                                                                   StartingAddress,
                                                                   Quantity,
                                                                   _dispatcherMock.Object,
                                                                   (_, _) => { },
                                                                   byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            new short[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedShortArrayProcessResponse!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsUShort()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsUShort(UnitIdentifier,
                                                                    StartingAddress,
                                                                    Quantity,
                                                                    _dispatcherMock.Object,
                                                                    (_, _) => { },
                                                                    byteOrder: ByteOrder),
                            ModbusFunctionCode.ReadHoldingRegisters,
                            Quantity,
                            new ushort[] { 1, 2 },
                            BytesPer16BitValue,
                            () => _capturedUShortArrayProcessResponse!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsInt()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                                                 StartingAddress,
                                                                 Count,
                                                                 _dispatcherMock.Object,
                                                                 (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsUInt()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsUInt(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _dispatcherMock.Object,
                                                                  (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsFloat()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsFloat(UnitIdentifier,
                                                                   StartingAddress,
                                                                   Count,
                                                                   _dispatcherMock.Object,
                                                                   (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsLong()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsLong(UnitIdentifier,
                                                                  StartingAddress,
                                                                  Count,
                                                                  _dispatcherMock.Object,
                                                                  (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsULong()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsULong(UnitIdentifier,
                                                                   StartingAddress,
                                                                   Count,
                                                                   _dispatcherMock.Object,
                                                                   (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
        public void ReadHoldingRegistersAsDouble()
        {
            // Act & Assert
            ReadRegistersAs(() => _sut.ReadHoldingRegistersAsDouble(UnitIdentifier,
                                                                    StartingAddress,
                                                                    Count,
                                                                    _dispatcherMock.Object,
                                                                    (_, _) => { },
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
        [TestProperty("spec", "AC-MODB-001.10")]
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
            _sut.ReadHoldingRegistersAsString(UnitIdentifier,
                                              StartingAddress,
                                              Quantity,
                                              _dispatcherMock.Object,
                                              (_, _) => { },
                                              textEncoding: textEncoding);

            // Assert
            VerifyCreateReadSingleInvoked<string>(ModbusFunctionCode.ReadHoldingRegisters, Quantity);
            var result = _capturedStringProcessResponse!(RegisterBytes);
            _dataConverterMock.Verify(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), textEncoding), Times.Once);
            Assert.AreEqual(expectedString, result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteSingleHoldingRegisterAsShort()
        {
            // Arrange
            const short value = 42;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier, StartingAddress, value, _dispatcherMock.Object, byteOrder: ByteOrder);

            // Assert
            _dataConverterMock.Verify(converter => converter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleRegister, RegisterBytes);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteSingleHoldingRegisterAsUShort()
        {
            // Arrange
            const ushort value = 42;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier, StartingAddress, value, _dispatcherMock.Object, byteOrder: ByteOrder);

            // Assert
            _dataConverterMock.Verify(converter => converter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteSingleRegister, RegisterBytes);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersRaw()
        {
            // Arrange

            // Act
            _sut.WriteMultipleHoldingRegistersRaw(UnitIdentifier, StartingAddress, RegisterBytes, _dispatcherMock.Object);

            // Assert
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleRegisters, RegisterBytes);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsShort()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsShort(UnitIdentifier, StartingAddress, values, _dispatcherMock.Object, byteOrder: ByteOrder),
                                    new short[] { 1, 2 });
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsUShort()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsUShort(UnitIdentifier, StartingAddress, values, _dispatcherMock.Object, byteOrder: ByteOrder),
                                    new ushort[] { 1, 2 });
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsInt()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsInt(UnitIdentifier,
                                                                                      StartingAddress,
                                                                                      values,
                                                                                      _dispatcherMock.Object,
                                                                                      byteOrder: ByteOrder,
                                                                                      wordOrder: WordOrder32),
                                    [1, 2],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsUInt()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsUInt(UnitIdentifier,
                                                                                       StartingAddress,
                                                                                       values,
                                                                                       _dispatcherMock.Object,
                                                                                       byteOrder: ByteOrder,
                                                                                       wordOrder: WordOrder32),
                                    [1U, 2U],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsFloat()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsFloat(UnitIdentifier,
                                                                                        StartingAddress,
                                                                                        values,
                                                                                        _dispatcherMock.Object,
                                                                                        byteOrder: ByteOrder,
                                                                                        wordOrder: WordOrder32),
                                    [1.1f, 2.2f],
                                    VerifySwapWords32Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsLong()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsLong(UnitIdentifier,
                                                                                       StartingAddress,
                                                                                       values,
                                                                                       _dispatcherMock.Object,
                                                                                       byteOrder: ByteOrder,
                                                                                       wordOrder: WordOrder64),
                                    [1L, 2L],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsULong()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsULong(UnitIdentifier,
                                                                                        StartingAddress,
                                                                                        values,
                                                                                        _dispatcherMock.Object,
                                                                                        byteOrder: ByteOrder,
                                                                                        wordOrder: WordOrder64),
                                    [1UL, 2UL],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public void WriteMultipleHoldingRegistersAsDouble()
        {
            // Act & Assert
            WriteHoldingRegistersAs(values => _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier,
                                                                                         StartingAddress,
                                                                                         values,
                                                                                         _dispatcherMock.Object,
                                                                                         byteOrder: ByteOrder,
                                                                                         wordOrder: WordOrder64),
                                    [1.1, 2.2],
                                    VerifySwapWords64Invoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public void WriteMultipleHoldingRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            const string value = "test";

            // Act
            _sut.WriteMultipleHoldingRegistersAsString(UnitIdentifier, StartingAddress, value, _dispatcherMock.Object, textEncoding: textEncoding);

            // Assert
            _dataConverterMock.Verify(converter => converter.ConvertStringToBytes(value, textEncoding), Times.Once);
            VerifyCreateWriteInvoked(ModbusFunctionCode.WriteMultipleRegisters, RegisterBytes);
        }

        private void DrainDispatcher()
        {
            var pending = _pendingDispatcherActions.ToArray();
            _pendingDispatcherActions.Clear();
            foreach (var action in pending)
            {
                action();
            }
        }

        private void InvokeMethod(TargetMethod targetMethod, Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            switch (targetMethod)
            {
                case TargetMethod.ReadDiscreteInputs:
                    _sut.ReadDiscreteInputs(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            (_, _) => { },
                                            errorCallback);
                    break;
                case TargetMethod.ReadCoils:
                    _sut.ReadCoils(UnitIdentifier,
                                   StartingAddress,
                                   Quantity,
                                   _dispatcherMock.Object,
                                   (_, _) => { },
                                   errorCallback);
                    break;
                case TargetMethod.WriteSingleCoil:
                    _sut.WriteSingleCoil(UnitIdentifier, StartingAddress, true, _dispatcherMock.Object, errorCallback: errorCallback);
                    break;
                case TargetMethod.WriteMultipleCoils:
                    _sut.WriteMultipleCoils(UnitIdentifier, StartingAddress, [true, false], _dispatcherMock.Object, errorCallback: errorCallback);
                    break;
                case TargetMethod.ReadInputRegistersAsFloat:
                    _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                                   StartingAddress,
                                                   Count,
                                                   _dispatcherMock.Object,
                                                   (_, _) => { },
                                                   errorCallback,
                                                   ByteOrder,
                                                   WordOrder32);
                    break;
                case TargetMethod.ReadHoldingRegistersAsInt:
                    _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                                   StartingAddress,
                                                   Count,
                                                   _dispatcherMock.Object,
                                                   (_, _) => { },
                                                   errorCallback,
                                                   ByteOrder,
                                                   WordOrder32);
                    break;
                case TargetMethod.WriteMultipleHoldingRegistersAsDouble:
                    _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier,
                                                               StartingAddress,
                                                               [1.1, 2.2, 3.3],
                                                               _dispatcherMock.Object,
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
                                                                           It.IsAny<TimeSpan?>(),
                                                                           It.IsAny<Func<Memory<byte>, T[]>>(),
                                                                           It.IsAny<IActorDispatcher>(),
                                                                           It.IsAny<Action<T[], ModbusReceipt>>(),
                                                                           It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                           It.IsAny<ModbusLinkAccumulator>()))
                               .Callback<ModbusFunctionCode, int, ushort, ushort, TimeSpan, TimeSpan?, Func<Memory<byte>, T[]>, IActorDispatcher, Action<T[], ModbusReceipt>,
                                   Action<Exception, ModbusReceipt>?, ModbusLinkAccumulator>((_,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              processResponse,
                                                                                              _,
                                                                                              _,
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
                                                                           It.IsAny<TimeSpan?>(),
                                                                           It.IsAny<Func<Memory<byte>, T>>(),
                                                                           It.IsAny<IActorDispatcher>(),
                                                                           It.IsAny<Action<T, ModbusReceipt>>(),
                                                                           It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                           It.IsAny<ModbusLinkAccumulator>()))
                               .Callback<ModbusFunctionCode, int, ushort, ushort, TimeSpan, TimeSpan?, Func<Memory<byte>, T>, IActorDispatcher, Action<T, ModbusReceipt>,
                                   Action<Exception, ModbusReceipt>?, ModbusLinkAccumulator>((_,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              processResponse,
                                                                                              _,
                                                                                              _,
                                                                                              _,
                                                                                              _) => capture(processResponse))
                               .Returns(ReadRequestStub);
        }

        private void SetupWriteCapture()
        {
            _requestFactoryMock.Setup(factory => factory.CreateWriteRequest(It.IsAny<ModbusFunctionCode>(),
                                                                            It.IsAny<int>(),
                                                                            It.IsAny<ushort>(),
                                                                            It.IsAny<byte[]>(),
                                                                            It.IsAny<TimeSpan>(),
                                                                            It.IsAny<TimeSpan?>(),
                                                                            It.IsAny<IActorDispatcher>(),
                                                                            It.IsAny<Action<ModbusReceipt>?>(),
                                                                            It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                            It.IsAny<ModbusLinkAccumulator>()))
                               .Returns(WriteRequestStub);
        }

        private void VerifyCreateReadArrayInvoked<T>(ModbusFunctionCode expectedFunctionCode, ushort expectedQuantity)
        {
            _requestFactoryMock.Verify(factory => factory.CreateReadRequest(expectedFunctionCode,
                                                                            UnitIdentifier,
                                                                            StartingAddress,
                                                                            expectedQuantity,
                                                                            It.IsAny<TimeSpan>(),
                                                                            It.IsAny<TimeSpan?>(),
                                                                            It.IsAny<Func<Memory<byte>, T[]>>(),
                                                                            It.IsAny<IActorDispatcher>(),
                                                                            It.IsAny<Action<T[], ModbusReceipt>>(),
                                                                            It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                            It.IsAny<ModbusLinkAccumulator>()),
                                       Times.Once);
        }

        private void VerifyCreateReadSingleInvoked<T>(ModbusFunctionCode expectedFunctionCode, ushort expectedQuantity)
        {
            _requestFactoryMock.Verify(factory => factory.CreateReadRequest(expectedFunctionCode,
                                                                            UnitIdentifier,
                                                                            StartingAddress,
                                                                            expectedQuantity,
                                                                            It.IsAny<TimeSpan>(),
                                                                            It.IsAny<TimeSpan?>(),
                                                                            It.IsAny<Func<Memory<byte>, T>>(),
                                                                            It.IsAny<IActorDispatcher>(),
                                                                            It.IsAny<Action<T, ModbusReceipt>>(),
                                                                            It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                            It.IsAny<ModbusLinkAccumulator>()),
                                       Times.Once);
        }

        private void VerifyCreateWriteInvoked(ModbusFunctionCode expectedFunctionCode, byte[] expectedData)
        {
            _requestFactoryMock.Verify(factory => factory.CreateWriteRequest(expectedFunctionCode,
                                                                             UnitIdentifier,
                                                                             StartingAddress,
                                                                             expectedData,
                                                                             It.IsAny<TimeSpan>(),
                                                                             It.IsAny<TimeSpan?>(),
                                                                             It.IsAny<IActorDispatcher>(),
                                                                             It.IsAny<Action<ModbusReceipt>?>(),
                                                                             It.IsAny<Action<Exception, ModbusReceipt>?>(),
                                                                             It.IsAny<ModbusLinkAccumulator>()),
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