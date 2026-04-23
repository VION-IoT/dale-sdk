using System;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Rtu.Test
{
    [TestClass]
    public class ModbusRtuRequestFactoryShould
    {
        private const ModbusFunctionCode ReadFunctionCode = ModbusFunctionCode.ReadHoldingRegisters;

        private const ModbusFunctionCode WriteFunctionCode = ModbusFunctionCode.WriteMultipleRegisters;

        private const int UnitIdentifier = 7;

        private const ushort StartingAddress = 0x0010;

        private const ushort Quantity = 4;

        private const ushort WriteAddress = 0x0020;

        private const int SingleResult = 42;

        private static readonly byte[] ResponseData = [0x01, 0x02, 0x03, 0x04];

        private static readonly byte[] WriteData = [0xAA, 0xBB];

        private static readonly int[] ArrayResult = [10, 20];

        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

        private static readonly DateTime CreatedAt = new(2026,
                                                         1,
                                                         1,
                                                         0,
                                                         0,
                                                         0,
                                                         DateTimeKind.Utc);

        private static readonly DateTime ExpiresAt = CreatedAt + OperationTimeout;

        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();

        private readonly Mock<ILogger<ModbusRtuRequestFactory>> _loggerMock = new();

        private int[]? _arraySuccessCallbackInput;

        private Exception? _errorCallbackInput;

        private int? _singleSuccessCallbackInput;

        private ModbusRtuRequestFactory _sut = null!;

        private bool _writeSuccessCallbackInvoked;

        [TestInitialize]
        public void Initialize()
        {
            _dateTimeProviderMock.Setup(provider => provider.UtcNow).Returns(CreatedAt);
            _dateTimeProviderMock.Setup(provider => provider.Add(CreatedAt, OperationTimeout)).Returns(ExpiresAt);
            _sut = new ModbusRtuRequestFactory(_dateTimeProviderMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public void PopulateReadArrayRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => ArrayResult,
                                                 input => _arraySuccessCallbackInput = input,
                                                 null);

            // Assert
            AssertReadRequestParameters(request);
        }

        [TestMethod]
        public void InvokeSuccessCallbackWithProcessedResultWhenReadArrayCallbackSucceeds()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => ArrayResult,
                                                 input => _arraySuccessCallbackInput = input,
                                                 null);

            // Act
            request.Callback(ResponseData, null);

            // Assert
            CollectionAssert.AreEqual(ArrayResult, _arraySuccessCallbackInput);
        }

        [TestMethod]
        public void InvokeErrorCallbackWhenReadArrayCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("transport failure");
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => ArrayResult,
                                                 input => _arraySuccessCallbackInput = input,
                                                 exception => _errorCallbackInput = exception);

            // Act
            request.Callback(null, expectedException);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsNull(_arraySuccessCallbackInput);
        }

        [TestMethod]
        public void InvokeErrorCallbackWhenArrayProcessResponseThrows()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 (Func<Memory<byte>, int[]>)(_ => throw new InvalidOperationException("processing failed")),
                                                 input => _arraySuccessCallbackInput = input,
                                                 exception => _errorCallbackInput = exception);

            // Act
            request.Callback(ResponseData, null);

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(_errorCallbackInput);
            Assert.IsNull(_arraySuccessCallbackInput);
        }

        [TestMethod]
        public void NotThrowWhenReadArrayTransportFailsAndErrorCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => ArrayResult,
                                                 input => _arraySuccessCallbackInput = input,
                                                 null);

            // Act / Assert
            request.Callback(null, new Exception());
        }

        [TestMethod]
        public void NotThrowWhenReadArrayProcessResponseThrowsAndErrorCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 (Func<Memory<byte>, int[]>)(_ => throw new InvalidOperationException("processing failed")),
                                                 input => _arraySuccessCallbackInput = input,
                                                 null);

            // Act / Assert
            request.Callback(ResponseData, null);
        }

        [TestMethod]
        public void PopulateReadSingleRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => SingleResult,
                                                 input => _singleSuccessCallbackInput = input,
                                                 null);

            // Assert
            AssertReadRequestParameters(request);
        }

        [TestMethod]
        public void InvokeSuccessCallbackWithProcessedResultWhenReadSingleCallbackSucceeds()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => SingleResult,
                                                 input => _singleSuccessCallbackInput = input,
                                                 null);

            // Act
            request.Callback(ResponseData, null);

            // Assert
            Assert.AreEqual(SingleResult, _singleSuccessCallbackInput);
        }

        [TestMethod]
        public void InvokeErrorCallbackWhenReadSingleCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("transport failure");
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => SingleResult,
                                                 input => _singleSuccessCallbackInput = input,
                                                 exception => _errorCallbackInput = exception);

            // Act
            request.Callback(null, expectedException);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsNull(_singleSuccessCallbackInput);
        }

        [TestMethod]
        public void InvokeErrorCallbackWhenSingleProcessResponseThrows()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 (Func<Memory<byte>, int>)(_ => throw new InvalidOperationException("processing failed")),
                                                 input => _singleSuccessCallbackInput = input,
                                                 exception => _errorCallbackInput = exception);

            // Act
            request.Callback(ResponseData, null);

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(_errorCallbackInput);
            Assert.IsNull(_singleSuccessCallbackInput);
        }

        [TestMethod]
        public void NotThrowWhenReadSingleTransportFailsAndErrorCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 _ => SingleResult,
                                                 input => _singleSuccessCallbackInput = input,
                                                 null);

            // Act / Assert
            request.Callback(null, new Exception());
        }

        [TestMethod]
        public void NotThrowWhenReadSingleProcessResponseThrowsAndErrorCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateReadRequest(ReadFunctionCode,
                                                 UnitIdentifier,
                                                 StartingAddress,
                                                 Quantity,
                                                 OperationTimeout,
                                                 (Func<Memory<byte>, int>)(_ => throw new InvalidOperationException("processing failed")),
                                                 input => _singleSuccessCallbackInput = input,
                                                 null);

            // Act / Assert
            request.Callback(ResponseData, null);
        }

        [TestMethod]
        public void PopulateWriteRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = _sut.CreateWriteRequest(WriteFunctionCode,
                                                  UnitIdentifier,
                                                  WriteAddress,
                                                  WriteData,
                                                  OperationTimeout,
                                                  null,
                                                  null);

            // Assert
            Assert.AreEqual(WriteFunctionCode, request.FunctionCode);
            Assert.AreEqual((byte)UnitIdentifier, request.UnitId);
            Assert.AreEqual(WriteAddress, request.Address);
            Assert.AreSame(WriteData, request.Data);
            Assert.AreEqual(CreatedAt, request.CreatedAt);
            Assert.AreEqual(ExpiresAt, request.ExpiresAt);
            Assert.AreNotEqual(Guid.Empty, request.CorrelationId);
        }

        [TestMethod]
        public void InvokeSuccessCallbackWhenWriteCallbackSucceeds()
        {
            // Arrange
            var request = _sut.CreateWriteRequest(WriteFunctionCode,
                                                  UnitIdentifier,
                                                  WriteAddress,
                                                  WriteData,
                                                  OperationTimeout,
                                                  () => _writeSuccessCallbackInvoked = true,
                                                  null);

            // Act
            request.Callback(null);

            // Assert
            Assert.IsTrue(_writeSuccessCallbackInvoked);
        }

        [TestMethod]
        public void InvokeErrorCallbackWhenWriteCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("write failure");
            var request = _sut.CreateWriteRequest(WriteFunctionCode,
                                                  UnitIdentifier,
                                                  WriteAddress,
                                                  WriteData,
                                                  OperationTimeout,
                                                  () => _writeSuccessCallbackInvoked = true,
                                                  exception => _errorCallbackInput = exception);

            // Act
            request.Callback(expectedException);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsFalse(_writeSuccessCallbackInvoked);
        }

        [TestMethod]
        public void NotThrowWhenWriteSucceedsAndSuccessCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateWriteRequest(WriteFunctionCode,
                                                  UnitIdentifier,
                                                  WriteAddress,
                                                  WriteData,
                                                  OperationTimeout,
                                                  null,
                                                  null);

            // Act / Assert
            request.Callback(null);
        }

        [TestMethod]
        public void NotThrowWhenWriteFailsAndErrorCallbackIsNull()
        {
            // Arrange
            var request = _sut.CreateWriteRequest(WriteFunctionCode,
                                                  UnitIdentifier,
                                                  WriteAddress,
                                                  WriteData,
                                                  OperationTimeout,
                                                  null,
                                                  null);

            // Act / Assert
            request.Callback(new Exception());
        }

        private static void AssertReadRequestParameters(ReadModbusRtuRequest request)
        {
            Assert.AreEqual(ReadFunctionCode, request.FunctionCode);
            Assert.AreEqual((byte)UnitIdentifier, request.UnitId);
            Assert.AreEqual(StartingAddress, request.StartingAddress);
            Assert.AreEqual(Quantity, request.Quantity);
            Assert.AreEqual(CreatedAt, request.CreatedAt);
            Assert.AreEqual(ExpiresAt, request.ExpiresAt);
            Assert.AreNotEqual(Guid.Empty, request.CorrelationId);
        }
    }
}