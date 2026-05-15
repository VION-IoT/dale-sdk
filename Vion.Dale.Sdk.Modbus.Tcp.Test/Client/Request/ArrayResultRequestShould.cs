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
    public class ArrayResultRequestShould
    {
        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger> _loggerMock = new();

        private readonly string _requestName = Guid.NewGuid().ToString();

        private readonly int[] _successOperationResult = [1, 2, 3];

        private Action? _capturedDispatcherAction;

        private Exception? _errorCallbackInput;

        private int[]? _successCallbackInput;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedDispatcherAction = action);
        }

        [TestMethod]
        public async Task HaveRequestName()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreEqual(_requestName, sut.Name);
        }

        [TestMethod]
        public async Task HaveRequestId()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);

            // Assert
            Assert.AreNotEqual(Guid.Empty, sut.Id);
        }

        [TestMethod]
        public async Task PassSuccessCallbackToDispatcherWhenOperationSucceeds()
        {
            // Arrange
            var sut = CreateArrayResultRequest(SuccessfulOperation());

            // Act
            await sut.ExecuteAsync(CancellationToken.None);
            _capturedDispatcherAction?.Invoke();

            // Assert
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            CollectionAssert.AreEqual(_successOperationResult, _successCallbackInput);
        }

        [TestMethod]
        public async Task PassErrorCallbackToDispatcherWhenOperationFails()
        {
            // Arrange
            var sut = CreateArrayResultRequest(FailingOperation(), ErrorCallback());

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
            var sut = CreateArrayResultRequest(FailingOperation());

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
            var sut = operationSucceeds ? CreateArrayResultRequest(SuccessfulOperation()) : CreateArrayResultRequest(FailingOperation(), ErrorCallback());
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws(new Exception());

            // Act / Assert
            await sut.ExecuteAsync(CancellationToken.None);
        }

        private ArrayResultRequest<int> CreateArrayResultRequest(Func<CancellationToken, Task<int[]>> operation, Action<Exception>? errorCallback = null)
        {
            return new ArrayResultRequest<int>(_requestName,
                                               _dispatcherMock.Object,
                                               operation,
                                               input => _successCallbackInput = input,
                                               errorCallback,
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

        private Action<Exception> ErrorCallback()
        {
            return exception => _errorCallbackInput = exception;
        }
    }
}