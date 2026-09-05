using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.Request
{
    [TestClass]
    public class ArrayResultRequestShould
    {
        private readonly ModbusLinkAccumulator _accumulator = new();

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger> _loggerMock = new();

        private readonly string _requestName = Guid.NewGuid().ToString();

        private readonly int[] _successOperationResult = [1, 2, 3];

        private readonly FakeTimeProvider _timeProvider = new();

        private Action? _capturedDispatcherAction;

        private Exception? _errorCallbackInput;

        private ModbusReceipt? _receipt;

        private int[]? _successCallbackInput;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedDispatcherAction = action);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public async Task HaveRequestName()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);

            // Assert
            Assert.AreEqual(_requestName, sut.Name);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public async Task HaveRequestId()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);

            // Assert
            Assert.AreNotEqual(Guid.Empty, sut.Id);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public async Task PassSuccessCallbackToDispatcherWhenOperationSucceeds()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            CollectionAssert.AreEqual(_successOperationResult, _successCallbackInput);
            Assert.AreEqual(ModbusOutcome.Success, _receipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public async Task PassErrorCallbackToDispatcherWhenOperationFails()
        {
            // Arrange
            var sut = CreateArrayResultRequest(FailingOperation(), ErrorCallback());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsInstanceOfType<ConnectionTimeoutException>(_errorCallbackInput);
            Assert.AreEqual(ModbusOutcome.Timeout, _receipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public async Task NotInvokeDispatcherWhenOperationFailsAndErrorCallbackNull()
        {
            // Arrange
            var sut = CreateArrayResultRequest(FailingOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.8")]
        [DataRow(true, DisplayName = "When operation succeeds")]
        [DataRow(false, DisplayName = "When operation fails")]
        public async Task NotThrowExceptionWhenDispatcherInvocationFails(bool operationSucceeds)
        {
            // Arrange
            var sut = operationSucceeds ? CreateArrayResultRequest(SuccessfulOperation()) : CreateArrayResultRequest(FailingOperation(), ErrorCallback());
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws(new Exception());

            // Act / Assert
            await sut.ExecuteAsync(CancellationToken.None, null);
        }

        private ArrayResultRequest<int> CreateArrayResultRequest(Func<CancellationToken, Task<int[]>> operation, Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            return new ArrayResultRequest<int>(_requestName,
                                               _dispatcherMock.Object,
                                               operation,
                                               (input, receipt) =>
                                               {
                                                   _successCallbackInput = input;
                                                   _receipt = receipt;
                                               },
                                               errorCallback,
                                               _timeProvider,
                                               _accumulator,
                                               _loggerMock.Object);
        }

        private Func<CancellationToken, Task<int[]>> SuccessfulOperation()
        {
            return _ => Task.FromResult(_successOperationResult);
        }

        private static Func<CancellationToken, Task<int[]>> FailingOperation()
        {
            return _ => throw new ConnectionTimeoutException(2);
        }

        private Action<Exception, ModbusReceipt> ErrorCallback()
        {
            return (exception, receipt) =>
                   {
                       _errorCallbackInput = exception;
                       _receipt = receipt;
                   };
        }
    }
}