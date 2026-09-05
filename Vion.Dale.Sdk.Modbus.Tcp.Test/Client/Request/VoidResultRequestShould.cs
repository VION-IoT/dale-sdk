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
    public class VoidResultRequestShould
    {
        private readonly ModbusLinkAccumulator _accumulator = new();

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger> _loggerMock = new();

        private readonly string _requestName = Guid.NewGuid().ToString();

        private readonly FakeTimeProvider _timeProvider = new();

        private Action? _capturedDispatcherAction;

        private Exception? _errorCallbackInput;

        private ModbusReceipt? _receipt;

        private bool _successCallbackInvoked;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedDispatcherAction = action);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.7")]
        public void CompleteOnlyOnceWhenFailedRepeatedly()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation(), errorCallback: ErrorCallback());

            // Act
            sut.HandleRequestFailed(new RequestDroppedException(_requestName, 1, QueueOverflowPolicy.RejectNew));
            sut.HandleRequestFailed(new RequestDroppedException(_requestName));

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.AreEqual(1L, _accumulator.Snapshot(0).DroppedCount);
            Assert.AreEqual(ModbusOutcome.Dropped, _accumulator.Snapshot(0).LastFailureOutcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.7")]
        public async Task CompleteOnlyOnceWhenFailedAfterItSucceeded()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());
            await sut.ExecuteAsync(CancellationToken.None, null);

            // Act
            sut.HandleRequestFailed(new RequestDroppedException(_requestName));

            // Assert
            Assert.AreEqual(1L, _accumulator.Snapshot(0).SuccessCount);
            Assert.AreEqual(0L, _accumulator.Snapshot(0).DroppedCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.10")]
        public async Task HaveRequestName()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());

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
            var sut = CreateVoidResultRequest(SuccessfulOperation());

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
            var sut = CreateVoidResultRequest(SuccessfulOperation(), SuccessCallback());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsTrue(_successCallbackInvoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.5")]
        public async Task NotInvokeDispatcherWhenOperationSucceedsAndSuccessCallbackNull()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
            Assert.IsFalse(_successCallbackInvoked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public async Task PassErrorCallbackToDispatcherWhenOperationFails()
        {
            // Arrange
            var sut = CreateVoidResultRequest(FailingOperation(), errorCallback: ErrorCallback());

            // Act
            await sut.ExecuteAsync(CancellationToken.None, null);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsInstanceOfType<ConnectionTimeoutException>(_errorCallbackInput);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.3")]
        public async Task NotInvokeDispatcherWhenOperationFailsAndErrorCallbackNull()
        {
            // Arrange
            var sut = CreateVoidResultRequest(FailingOperation());

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
            var sut = operationSucceeds ? CreateVoidResultRequest(SuccessfulOperation(), SuccessCallback()) :
                          CreateVoidResultRequest(FailingOperation(), errorCallback: ErrorCallback());
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws(new Exception());

            // Act / Assert
            await sut.ExecuteAsync(CancellationToken.None, null);
        }

        private VoidResultRequest CreateVoidResultRequest(Func<CancellationToken, Task> operation,
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null)
        {
            return new VoidResultRequest(_requestName,
                                         _dispatcherMock.Object,
                                         operation,
                                         successCallback,
                                         errorCallback,
                                         _timeProvider,
                                         _accumulator,
                                         _loggerMock.Object);
        }

        private static Func<CancellationToken, Task> SuccessfulOperation()
        {
            return _ => Task.CompletedTask;
        }

        private Action<ModbusReceipt> SuccessCallback()
        {
            return receipt =>
                   {
                       _successCallbackInvoked = true;
                       _receipt = receipt;
                   };
        }

        private static Func<CancellationToken, Task> FailingOperation()
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