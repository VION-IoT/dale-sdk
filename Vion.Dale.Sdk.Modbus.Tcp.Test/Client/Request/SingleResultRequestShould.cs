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
    public class SingleResultRequestShould
    {
        private const int SuccessOperationResult = 42;

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger> _loggerMock = new();

        private readonly string _requestName = Guid.NewGuid().ToString();

        private Action? _capturedDispatcherAction;

        private Exception? _errorCallbackInput;

        private int? _successCallbackInput;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedDispatcherAction = action);
        }

        [TestMethod]
        public async Task HaveRequestName()
        {
            // Arrange
            var sut = CreateSingleResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreEqual(_requestName, sut.Name);
        }

        [TestMethod]
        public async Task HaveRequestId()
        {
            // Arrange
            var sut = CreateSingleResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreNotEqual(Guid.Empty, sut.Id);
        }

        [TestMethod]
        public async Task PassSuccessCallbackToDispatcherWhenOperationSucceeds()
        {
            // Arrange
            var sut = CreateSingleResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.AreEqual(SuccessOperationResult, _successCallbackInput);
        }

        [TestMethod]
        public async Task PassErrorCallbackToDispatcherWhenOperationFails()
        {
            // Arrange
            var sut = CreateSingleResultRequest(FailingOperation(), ErrorCallback());

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
            var sut = CreateSingleResultRequest(FailingOperation());

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
            var sut = operationSucceeds ? CreateSingleResultRequest(SuccessfulOperation()) : CreateSingleResultRequest(FailingOperation(), ErrorCallback());
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws(new Exception());

            // Act / Assert
            await sut.ExecuteAsync(CancellationToken.None);
        }

        private SingleResultRequest<int> CreateSingleResultRequest(Func<CancellationToken, Task<int>> operation, Action<Exception>? errorCallback = null)
        {
            return new SingleResultRequest<int>(_requestName,
                                                _dispatcherMock.Object,
                                                operation,
                                                input => _successCallbackInput = input,
                                                errorCallback,
                                                _loggerMock.Object);
        }

        private static Func<CancellationToken, Task<int>> SuccessfulOperation()
        {
            return _ => Task.FromResult(SuccessOperationResult);
        }

        private static Func<CancellationToken, Task<int>> FailingOperation()
        {
            return _ => throw new ConnectionTimeoutException(2);
        }

        private Action<Exception> ErrorCallback()
        {
            return exception => _errorCallbackInput = exception;
        }
    }
}