using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.Request
{
    [TestClass]
    public class VoidResultRequestShould
    {
        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger> _loggerMock = new();

        private readonly string _requestName = Guid.NewGuid().ToString();

        private Action? _capturedDispatcherAction;

        private Exception? _errorCallbackInput;

        private bool _successCallbackInvoked;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedDispatcherAction = action);
        }

        [TestMethod]
        public async Task HaveRequestName()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreEqual(_requestName, sut.Name);
        }

        [TestMethod]
        public async Task HaveRequestId()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreNotEqual(Guid.Empty, sut.Id);
        }

        [TestMethod]
        public async Task PassSuccessCallbackToDispatcherWhenOperationSucceeds()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation(), SuccessCallback());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsTrue(_successCallbackInvoked);
        }

        [TestMethod]
        public async Task NotInvokeDispatcherWhenOperationSucceedsAndSuccessCallbackIsNull()
        {
            // Arrange
            var sut = CreateVoidResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
            Assert.IsFalse(_successCallbackInvoked);
        }

        [TestMethod]
        public async Task PassErrorCallbackToDispatcherWhenOperationFails()
        {
            // Arrange
            var sut = CreateVoidResultRequest(FailingOperation(), errorCallback: ErrorCallback());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsInstanceOfType<ConnectionTimeoutException>(_errorCallbackInput);
        }

        [TestMethod]
        public async Task NotInvokeDispatcherWhenOperationFailsAndErrorCallbackIsNull()
        {
            // Arrange
            var sut = CreateVoidResultRequest(FailingOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
        }

        [TestMethod]
        [DataRow(true, DisplayName = "When operation succeeds")]
        [DataRow(false, DisplayName = "When operation fails")]
        public async Task NotThrowExceptionWhenDispatcherInvocationFails(bool operationSucceeds)
        {
            // Arrange
            var sut = operationSucceeds ? CreateVoidResultRequest(SuccessfulOperation(), SuccessCallback()) :
                          CreateVoidResultRequest(FailingOperation(), errorCallback: ErrorCallback());
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws(new Exception());

            // Act / Assert
            await sut.ExecuteAsync(CancellationToken.None);
        }

        private VoidResultRequest CreateVoidResultRequest(Func<CancellationToken, Task> operation, Action? successCallback = null, Action<Exception>? errorCallback = null)
        {
            return new VoidResultRequest(_requestName,
                                         _dispatcherMock.Object,
                                         operation,
                                         successCallback,
                                         errorCallback,
                                         _loggerMock.Object);
        }

        private static Func<CancellationToken, Task> SuccessfulOperation()
        {
            return _ => Task.CompletedTask;
        }

        private Action SuccessCallback()
        {
            return () => _successCallbackInvoked = true;
        }

        private static Func<CancellationToken, Task> FailingOperation()
        {
            return _ => throw new ConnectionTimeoutException(2);
        }

        private Action<Exception> ErrorCallback()
        {
            return exception => _errorCallbackInput = exception;
        }
    }
}