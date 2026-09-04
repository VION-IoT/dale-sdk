using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

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

        private static readonly TimeSpan MaxQueuedAge = TimeSpan.FromSeconds(30);

        private static readonly DateTime CreatedAt = new(2026,
                                                         1,
                                                         1,
                                                         0,
                                                         0,
                                                         0,
                                                         DateTimeKind.Utc);

        private static readonly ModbusReceipt SuccessReceipt = new(CreatedAt.AddMilliseconds(25),
                                                                   250,
                                                                   TimeSpan.FromMilliseconds(20),
                                                                   TimeSpan.FromMilliseconds(5),
                                                                   ModbusOutcome.Success);

        private static readonly ModbusReceipt TimeoutReceipt = SuccessReceipt with { Outcome = ModbusOutcome.Timeout };

        private readonly ModbusLinkAccumulator _accumulator = new();

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger<ModbusRtuRequestFactory>> _loggerMock = new();

        private readonly FakeTimeProvider _timeProvider = new(CreatedAt);

        private int[]? _arraySuccessCallbackInput;

        private Exception? _errorCallbackInput;

        private ModbusReceipt? _receipt;

        private int? _singleSuccessCallbackInput;

        private ModbusRtuRequestFactory _sut = null!;

        private bool _writeSuccessCallbackInvoked;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new ModbusRtuRequestFactory(_timeProvider, _loggerMock.Object);

            // Callbacks now travel the caller's dispatcher, exactly as on Modbus TCP. Run them inline so these
            // tests keep asserting the factory's behaviour rather than the actor hop.
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => action());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.1")]
        public void RecordEveryCompletedTransactionInLinkAccumulator()
        {
            // Arrange
            var request = CreateArrayReadRequest(_ => ArrayResult);

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);
            request.Callback(null, new OperationTimeoutException(), TimeoutReceipt);

            // Assert
            var summary = _accumulator.Snapshot(0);
            Assert.AreEqual(1, summary.SuccessCount);
            Assert.AreEqual(1, summary.TimeoutCount);
            Assert.AreEqual(ModbusLinkState.Faulted, summary.State);
        }

        private ReadModbusRtuRequest CreateArrayReadRequest(Func<Memory<byte>, int[]> processResponse, Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            return _sut.CreateReadRequest(ReadFunctionCode,
                                          UnitIdentifier,
                                          StartingAddress,
                                          Quantity,
                                          OperationTimeout,
                                          MaxQueuedAge,
                                          processResponse,
                                          _dispatcherMock.Object,
                                          (input, receipt) =>
                                          {
                                              _arraySuccessCallbackInput = input;
                                              _receipt = receipt;
                                          },
                                          errorCallback,
                                          _accumulator);
        }

        private ReadModbusRtuRequest CreateSingleReadRequest(Func<Memory<byte>, int> processResponse, Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            return _sut.CreateReadRequest(ReadFunctionCode,
                                          UnitIdentifier,
                                          StartingAddress,
                                          Quantity,
                                          OperationTimeout,
                                          MaxQueuedAge,
                                          processResponse,
                                          _dispatcherMock.Object,
                                          (input, receipt) =>
                                          {
                                              _singleSuccessCallbackInput = input;
                                              _receipt = receipt;
                                          },
                                          errorCallback,
                                          _accumulator);
        }

        private WriteModbusRtuRequest CreateWriteRequest(Action<ModbusReceipt>? successCallback = null, Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            return _sut.CreateWriteRequest(WriteFunctionCode,
                                           UnitIdentifier,
                                           WriteAddress,
                                           WriteData,
                                           OperationTimeout,
                                           MaxQueuedAge,
                                           _dispatcherMock.Object,
                                           successCallback,
                                           errorCallback,
                                           _accumulator);
        }

        private static void AssertReadRequestParameters(ReadModbusRtuRequest request)
        {
            Assert.AreEqual(ReadFunctionCode, request.FunctionCode);
            Assert.AreEqual((byte)UnitIdentifier, request.UnitId);
            Assert.AreEqual(StartingAddress, request.StartingAddress);
            Assert.AreEqual(Quantity, request.Quantity);
            Assert.AreEqual(CreatedAt, request.CreatedAt);
            Assert.AreEqual(OperationTimeout, request.OperationTimeout);
            Assert.AreEqual(MaxQueuedAge, request.MaxQueuedAge);
            Assert.AreNotEqual(Guid.Empty, request.CorrelationId);
        }

        #region Read - array result

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        public void PopulateReadArrayRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = CreateArrayReadRequest(_ => ArrayResult);

            // Assert
            AssertReadRequestParameters(request);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeSuccessCallbackWithProcessedResultWhenReadArrayCallbackSucceeds()
        {
            // Arrange
            var request = CreateArrayReadRequest(_ => ArrayResult);

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            CollectionAssert.AreEqual(ArrayResult, _arraySuccessCallbackInput);
            Assert.AreEqual(SuccessReceipt, _receipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeErrorCallbackWhenReadArrayCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("transport failure");
            var request = CreateArrayReadRequest(_ => ArrayResult,
                                                 (exception, receipt) =>
                                                 {
                                                     _errorCallbackInput = exception;
                                                     _receipt = receipt;
                                                 });

            // Act
            request.Callback(null, expectedException, TimeoutReceipt);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsNull(_arraySuccessCallbackInput);
            Assert.AreEqual(ModbusOutcome.Timeout, _receipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void InvokeErrorCallbackWhenArrayProcessResponseThrows()
        {
            // Arrange
            var request = CreateArrayReadRequest((Func<Memory<byte>, int[]>)(_ => throw new InvalidOperationException("processing failed")),
                                                 (exception, receipt) =>
                                                 {
                                                     _errorCallbackInput = exception;
                                                     _receipt = receipt;
                                                 });

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(_errorCallbackInput);
            Assert.IsNull(_arraySuccessCallbackInput);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void ReclassifySuccessAsProtocolErrorWhenArrayProcessResponseThrows()
        {
            // Arrange — the device answered, so the handler stamped Success; reading its answer is what failed.
            var request = CreateArrayReadRequest((Func<Memory<byte>, int[]>)(_ => throw new ModbusResponseAlignmentException(UnitIdentifier, StartingAddress, 5, 4)),
                                                 (exception, receipt) =>
                                                 {
                                                     _errorCallbackInput = exception;
                                                     _receipt = receipt;
                                                 });

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            Assert.AreEqual(ModbusOutcome.ProtocolError, _receipt!.Value.Outcome);
            Assert.AreEqual(1, _accumulator.Snapshot(0).ProtocolErrorCount);
            Assert.AreEqual(0, _accumulator.Snapshot(0).SuccessCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void ReclassifySuccessAsInvalidWhenArrayProcessResponseRejectsRequestedConversion()
        {
            // Arrange
            var request = CreateArrayReadRequest((Func<Memory<byte>, int[]>)(_ => throw new UnsupportedByteOrderException((ByteOrder)99)),
                                                 (exception, receipt) =>
                                                 {
                                                     _errorCallbackInput = exception;
                                                     _receipt = receipt;
                                                 });

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            Assert.AreEqual(ModbusOutcome.Invalid, _receipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.5")]
        public void NotThrowWhenReadArrayTransportFailsAndErrorCallbackNull()
        {
            // Arrange
            var request = CreateArrayReadRequest(_ => ArrayResult);

            // Act / Assert
            request.Callback(null, new Exception(), TimeoutReceipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void NotThrowWhenReadArrayProcessResponseThrowsAndErrorCallbackNull()
        {
            // Arrange
            var request = CreateArrayReadRequest((Func<Memory<byte>, int[]>)(_ => throw new InvalidOperationException("processing failed")));

            // Act / Assert
            request.Callback(ResponseData, null, SuccessReceipt);
        }

        #endregion

        #region Read - single result

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        public void PopulateReadSingleRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = CreateSingleReadRequest(_ => SingleResult);

            // Assert
            AssertReadRequestParameters(request);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeSuccessCallbackWithProcessedResultWhenReadSingleCallbackSucceeds()
        {
            // Arrange
            var request = CreateSingleReadRequest(_ => SingleResult);

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            Assert.AreEqual(SingleResult, _singleSuccessCallbackInput);
            Assert.AreEqual(SuccessReceipt, _receipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeErrorCallbackWhenReadSingleCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("transport failure");
            var request = CreateSingleReadRequest(_ => SingleResult,
                                                  (exception, receipt) =>
                                                  {
                                                      _errorCallbackInput = exception;
                                                      _receipt = receipt;
                                                  });

            // Act
            request.Callback(null, expectedException, TimeoutReceipt);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsNull(_singleSuccessCallbackInput);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void InvokeErrorCallbackWhenSingleProcessResponseThrows()
        {
            // Arrange
            var request = CreateSingleReadRequest((Func<Memory<byte>, int>)(_ => throw new InvalidOperationException("processing failed")),
                                                  (exception, receipt) =>
                                                  {
                                                      _errorCallbackInput = exception;
                                                      _receipt = receipt;
                                                  });

            // Act
            request.Callback(ResponseData, null, SuccessReceipt);

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(_errorCallbackInput);
            Assert.IsNull(_singleSuccessCallbackInput);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.5")]
        public void NotThrowWhenReadSingleTransportFailsAndErrorCallbackNull()
        {
            // Arrange
            var request = CreateSingleReadRequest(_ => SingleResult);

            // Act / Assert
            request.Callback(null, new Exception(), TimeoutReceipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-015.8")]
        public void NotThrowWhenReadSingleProcessResponseThrowsAndErrorCallbackNull()
        {
            // Arrange
            var request = CreateSingleReadRequest((Func<Memory<byte>, int>)(_ => throw new InvalidOperationException("processing failed")));

            // Act / Assert
            request.Callback(ResponseData, null, SuccessReceipt);
        }

        #endregion

        #region Write

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        public void PopulateWriteRequestWithProvidedParameters()
        {
            // Arrange

            // Act
            var request = CreateWriteRequest();

            // Assert
            Assert.AreEqual(WriteFunctionCode, request.FunctionCode);
            Assert.AreEqual((byte)UnitIdentifier, request.UnitId);
            Assert.AreEqual(WriteAddress, request.Address);
            Assert.AreSame(WriteData, request.Data);
            Assert.AreEqual(CreatedAt, request.CreatedAt);
            Assert.AreEqual(OperationTimeout, request.OperationTimeout);
            Assert.AreEqual(MaxQueuedAge, request.MaxQueuedAge);
            Assert.AreNotEqual(Guid.Empty, request.CorrelationId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeSuccessCallbackWhenWriteCallbackSucceeds()
        {
            // Arrange
            var request = CreateWriteRequest(receipt =>
                                             {
                                                 _writeSuccessCallbackInvoked = true;
                                                 _receipt = receipt;
                                             });

            // Act
            request.Callback(null, SuccessReceipt);

            // Assert
            Assert.IsTrue(_writeSuccessCallbackInvoked);
            Assert.AreEqual(SuccessReceipt, _receipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public void InvokeErrorCallbackWhenWriteCallbackReceivesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("write failure");
            var request = CreateWriteRequest(_ => _writeSuccessCallbackInvoked = true,
                                             (exception, receipt) =>
                                             {
                                                 _errorCallbackInput = exception;
                                                 _receipt = receipt;
                                             });

            // Act
            request.Callback(expectedException, TimeoutReceipt);

            // Assert
            Assert.AreSame(expectedException, _errorCallbackInput);
            Assert.IsFalse(_writeSuccessCallbackInvoked);
            Assert.AreEqual(ModbusOutcome.Timeout, _receipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.5")]
        public void NotThrowWhenWriteSucceedsAndSuccessCallbackNull()
        {
            // Arrange
            var request = CreateWriteRequest();

            // Act / Assert
            request.Callback(null, SuccessReceipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.5")]
        public void NotThrowWhenWriteFailsAndErrorCallbackNull()
        {
            // Arrange
            var request = CreateWriteRequest();

            // Act / Assert
            request.Callback(new Exception(), TimeoutReceipt);
        }

        #endregion
    }
}