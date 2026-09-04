using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.Request
{
    [TestClass]
    [SuppressMessage("Usage", "MSTEST0049:Flow TestContext.CancellationToken to async operations")]
    public class RequestQueueShould
    {
        private const string ArrayRequestName = "ArrayRequest";

        private const string SingleRequestName = "SinlgeRequest";

        private const string VoidRequestName = "VoidRequest";

        private readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        private readonly ModbusLinkAccumulator _accumulator = new();

        private readonly Func<CancellationToken, Task<int[]>> _arrayRequestOperation = _ => Task.FromResult(Array.Empty<int>());

        private readonly Action<int[], ModbusReceipt> _arraySuccessCallback = (_, _) => { };

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Mock<ILogger<RequestQueue>> _loggerMock = new();

        private readonly List<RequestDroppedException> _requestDroppedExceptions = [];

        private readonly Mock<IRequestFactory> _requestFactoryMock = new();

        private readonly Func<CancellationToken, Task<int>> _singleRequestOperation = _ => Task.FromResult(0);

        private readonly Action<int, ModbusReceipt> _singleSuccessCallback = (_, _) => { };

        private readonly List<string> _startedRequestNames = [];

        private readonly Func<CancellationToken, Task> _voidRequestOperation = _ => Task.CompletedTask;

        private readonly Action<ModbusReceipt> _voidSuccessCallback = _ => { };

        private CancellationTokenSource? _inflightRequestCts;

        private RequestQueue _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new RequestQueue(_requestFactoryMock.Object, _loggerMock.Object);
        }

        [TestCleanup]
        public async Task CleanupAsync()
        {
            if (_inflightRequestCts == null)
            {
                return;
            }

            await _inflightRequestCts.CancelAsync();
            _inflightRequestCts.Dispose();
        }

        [TestMethod]
        [DataRow(0, DisplayName = "Zero")]
        [DataRow(-1, DisplayName = "Negative")]
        public void ThrowExceptionWhenMaxQueuedAgeIsNotPositive(int seconds)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.MaxQueuedAge = TimeSpan.FromSeconds(seconds));
        }

        [TestMethod]
        public void KeepNoLimitWhenMaxQueuedAgeIsCleared()
        {
            // Arrange
            _sut.MaxQueuedAge = TimeSpan.FromSeconds(5);

            // Act
            _sut.MaxQueuedAge = null;

            // Assert
            Assert.IsNull(_sut.MaxQueuedAge);
        }

        [TestMethod]
        public void ThrowExceptionWhenAlreadyInitialized()
        {
            // Arrange
            _sut.Initialize(3, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Initialize(5, QueueOverflowPolicy.RejectNew, _accumulator));
        }

        [TestMethod]
        public void ThrowExceptionWhenOverflowPolicyUnsupported()
        {
            // Arrange
            const QueueOverflowPolicy unsupportedPolicy = (QueueOverflowPolicy)999;

            // Act / Assert
            Assert.Throws<NotSupportedException>(() => _sut.Initialize(5, unsupportedPolicy, _accumulator));
        }

        [TestMethod]
        public void ThrowExceptionWhenEnqueuingArrayResultRequestBeforeInitialization()
        {
            // Arrange
            Func<CancellationToken, Task<int[]>> operation = _ => Task.FromResult(Array.Empty<int>());

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, operation, _arraySuccessCallback, null));
        }

        [TestMethod]
        public void ThrowExceptionWhenEnqueuingSingleResultRequestBeforeInitialization()
        {
            // Arrange
            Func<CancellationToken, Task<int>> operation = _ => Task.FromResult(0);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, operation, _singleSuccessCallback, null));
        }

        [TestMethod]
        public void ThrowExceptionWhenEnqueuingVoidResultRequestBeforeInitialization()
        {
            // Arrange
            Func<CancellationToken, Task> operation = _ => Task.CompletedTask;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, operation, _voidSuccessCallback, null));
        }

        [TestMethod]
        public async Task ExecuteAllEnqueuedRequests()
        {
            // Arrange
            var arrayStartedTcs = SetupArrayResultRequest();
            var singleStartedTcs = SetupSingleRequestResult();
            var voidStartedTcs = SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            await Task.WhenAll(arrayStartedTcs.Task, singleStartedTcs.Task, voidStartedTcs.Task).WaitAsync(TestTimeout);

            // Assert
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == ArrayRequestName));
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == SingleRequestName));
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == VoidRequestName));
        }

        [TestMethod]
        public async Task ExecuteRequestsInFifoOrder()
        {
            // Arrange
            var arrayStartedTcs = SetupArrayResultRequest();
            var singleStartedTcs = SetupSingleRequestResult();
            var voidStartedTcs = SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            await Task.WhenAll(arrayStartedTcs.Task, singleStartedTcs.Task, voidStartedTcs.Task).WaitAsync(TestTimeout);

            // Assert
            Assert.AreEqual(ArrayRequestName, _startedRequestNames[0]);
            Assert.AreEqual(SingleRequestName, _startedRequestNames[1]);
            Assert.AreEqual(VoidRequestName, _startedRequestNames[2]);
        }

        [TestMethod]
        public async Task ContinueProcessingQueuedRequestsAfterFailure()
        {
            // Arrange
            var arrayStartedTcs = SetupArrayResultRequest(shouldThrow: true);
            var singleStartedTcs = SetupSingleRequestResult();
            var voidStartedTcs = SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            await Task.WhenAll(arrayStartedTcs.Task, singleStartedTcs.Task, voidStartedTcs.Task).WaitAsync(TestTimeout);

            // Assert
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == ArrayRequestName));
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == SingleRequestName));
            Assert.AreEqual(1, _startedRequestNames.Count(name => name == VoidRequestName));
        }

        [TestMethod]
        public async Task DropOldestRequestWhenQueueFull()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(2, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            _sut.Enqueue($"{VoidRequestName}-2", _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Assert
            Assert.HasCount(1, _requestDroppedExceptions);
            Assert.AreEqual(SingleRequestName, _requestDroppedExceptions[0].RequestName);
        }

        [TestMethod]
        public async Task DropNewestRequestWhenQueueFull()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(2, QueueOverflowPolicy.DropNewest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            _sut.Enqueue($"{VoidRequestName}-2", _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Assert
            Assert.HasCount(1, _requestDroppedExceptions);
            Assert.AreEqual(VoidRequestName, _requestDroppedExceptions[0].RequestName);
        }

        [TestMethod]
        public async Task RejectNewRequestWhenQueueFull()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(2, QueueOverflowPolicy.RejectNew, _accumulator);
            const string expectedRejectedRequestName = $"{VoidRequestName}-2";

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            _sut.Enqueue(expectedRejectedRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Assert
            Assert.HasCount(1, _requestDroppedExceptions);
            Assert.AreEqual(expectedRejectedRequestName, _requestDroppedExceptions[0].RequestName);
        }

        [TestMethod]
        public async Task ExcludeInFlightRequestFromQueuedCount()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(3, QueueOverflowPolicy.DropNewest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Assert
            Assert.AreEqual(2, _sut.QueuedRequestCount);
        }

        [TestMethod]
        public void ReportZeroQueuedCountWhenQueueEmpty()
        {
            // Arrange
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            var requestCount = _sut.QueuedRequestCount;

            // Assert
            Assert.AreEqual(0, requestCount);
        }

        [TestMethod]
        public async Task DecrementQueuedCountAfterRequestsComplete()
        {
            // Arrange
            var arrayStartedTcs = SetupArrayResultRequest();
            var singleStartedTcs = SetupSingleRequestResult();
            var voidStartedTcs = SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);
            await Task.WhenAll(arrayStartedTcs.Task, singleStartedTcs.Task, voidStartedTcs.Task).WaitAsync(TestTimeout);

            // Assert
            Assert.AreEqual(0, _sut.QueuedRequestCount);
        }

        [TestMethod]
        public async Task CompleteEveryStillQueuedRequestWhenDisposed()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Act
            _sut.Dispose();
            await _sut.ConsumerCompletion!;

            // Assert
            Assert.HasCount(2, _requestDroppedExceptions);
            Assert.AreEqual(SingleRequestName, _requestDroppedExceptions[0].RequestName);
            Assert.AreEqual(VoidRequestName, _requestDroppedExceptions[1].RequestName);
            Assert.IsTrue(_requestDroppedExceptions.TrueForAll(dropped => dropped.Reason == RequestDropReason.ClientDisposed));
        }

        [TestMethod]
        public async Task CompleteNothingWhenDisposedWithAnEmptyQueue()
        {
            // Arrange
            SetupArrayResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act
            _sut.Dispose();
            await _sut.ConsumerCompletion!;

            // Assert
            Assert.HasCount(0, _requestDroppedExceptions);
        }

        [TestMethod]
        public async Task CompleteAQueuedRequestOnlyOnceAcrossRepeatedDisposal()
        {
            // Arrange
            _inflightRequestCts = new CancellationTokenSource();
            var arrayStartedTcs = SetupArrayResultRequest(true, cancellationToken: _inflightRequestCts.Token);
            SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            await arrayStartedTcs.Task;
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Act
            _sut.Dispose();
            _sut.Dispose();
            await _sut.ConsumerCompletion!;

            // Assert
            Assert.HasCount(1, _requestDroppedExceptions);
        }

        [TestMethod]
        public void RejectNewRequestWhenQueueDisposed()
        {
            // Arrange
            SetupArrayResultRequest();
            SetupSingleRequestResult();
            SetupVoidResultRequest();
            _sut.Initialize(10, QueueOverflowPolicy.RejectNew, _accumulator);
            _sut.Dispose();

            // Act
            _sut.Enqueue(ArrayRequestName, _dispatcherMock.Object, _arrayRequestOperation, _arraySuccessCallback, null);
            _sut.Enqueue(SingleRequestName, _dispatcherMock.Object, _singleRequestOperation, _singleSuccessCallback, null);
            _sut.Enqueue(VoidRequestName, _dispatcherMock.Object, _voidRequestOperation, _voidSuccessCallback, null);

            // Assert
            Assert.HasCount(0, _startedRequestNames);
            Assert.HasCount(3, _requestDroppedExceptions);
            Assert.AreEqual(ArrayRequestName, _requestDroppedExceptions[0].RequestName);
            Assert.AreEqual(SingleRequestName, _requestDroppedExceptions[1].RequestName);
            Assert.AreEqual(VoidRequestName, _requestDroppedExceptions[2].RequestName);
        }

        [TestMethod]
        public void NotThrowIfDisposedMultipleTimes()
        {
            // Arrange
            _sut.Initialize(10, QueueOverflowPolicy.DropOldest, _accumulator);

            // Act / Assert
            _sut.Dispose();
            _sut.Dispose();
        }

        private TaskCompletionSource<bool> SetupArrayResultRequest(bool shouldBlock = false, bool shouldThrow = false, CancellationToken cancellationToken = default)
        {
            var executionStartedTcs = new TaskCompletionSource<bool>();
            _requestFactoryMock
                .Setup(factory => factory.Create(It.IsAny<string>(),
                                                 It.IsAny<IActorDispatcher>(),
                                                 It.IsAny<Func<CancellationToken, Task<int[]>>>(),
                                                 It.IsAny<Action<int[], ModbusReceipt>>(),
                                                 It.IsAny<Action<Exception, ModbusReceipt>>(),
                                                 It.IsAny<ModbusLinkAccumulator>(),
                                                 It.IsAny<ILogger>()))
                .Returns((string requestName,
                          IActorDispatcher _,
                          Func<CancellationToken, Task<int[]>> _,
                          Action<int[], ModbusReceipt> _,
                          Action<Exception, ModbusReceipt> _,
                          ModbusLinkAccumulator _,
                          ILogger _) => SetupRequest(requestName, executionStartedTcs, shouldBlock, shouldThrow, cancellationToken));

            return executionStartedTcs;
        }

        private TaskCompletionSource<bool> SetupSingleRequestResult(bool shouldBlock = false, bool shouldThrow = false, CancellationToken cancellationToken = default)
        {
            var executionStartedTcs = new TaskCompletionSource<bool>();
            _requestFactoryMock
                .Setup(factory => factory.Create(It.IsAny<string>(),
                                                 It.IsAny<IActorDispatcher>(),
                                                 It.IsAny<Func<CancellationToken, Task<int>>>(),
                                                 It.IsAny<Action<int, ModbusReceipt>>(),
                                                 It.IsAny<Action<Exception, ModbusReceipt>>(),
                                                 It.IsAny<ModbusLinkAccumulator>(),
                                                 It.IsAny<ILogger>()))
                .Returns((string requestName,
                          IActorDispatcher _,
                          Func<CancellationToken, Task<int>> _,
                          Action<int, ModbusReceipt> _,
                          Action<Exception, ModbusReceipt> _,
                          ModbusLinkAccumulator _,
                          ILogger _) => SetupRequest(requestName, executionStartedTcs, shouldBlock, shouldThrow, cancellationToken));

            return executionStartedTcs;
        }

        private TaskCompletionSource<bool> SetupVoidResultRequest(bool shouldBlock = false, bool shouldThrow = false, CancellationToken cancellationToken = default)
        {
            var executionStartedTcs = new TaskCompletionSource<bool>();
            _requestFactoryMock
                .Setup(factory => factory.Create(It.IsAny<string>(),
                                                 It.IsAny<IActorDispatcher>(),
                                                 It.IsAny<Func<CancellationToken, Task>>(),
                                                 It.IsAny<Action<ModbusReceipt>>(),
                                                 It.IsAny<Action<Exception, ModbusReceipt>>(),
                                                 It.IsAny<ModbusLinkAccumulator>(),
                                                 It.IsAny<ILogger>()))
                .Returns((string requestName,
                          IActorDispatcher _,
                          Func<CancellationToken, Task> _,
                          Action<ModbusReceipt> _,
                          Action<Exception, ModbusReceipt> _,
                          ModbusLinkAccumulator _,
                          ILogger _) => SetupRequest(requestName, executionStartedTcs, shouldBlock, shouldThrow, cancellationToken));

            return executionStartedTcs;
        }

        private IRequest SetupRequest(string requestName, TaskCompletionSource<bool> executionStartedTcs, bool shouldBlock, bool shouldThrow, CancellationToken cancellationToken)
        {
            var requestMock = new Mock<IRequest>();
            requestMock.SetupGet(request => request.Name).Returns(requestName);
            requestMock.Setup(request => request.ExecuteAsync(It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
                       .Callback(() =>
                                 {
                                     _startedRequestNames.Add(requestName);
                                     executionStartedTcs.SetResult(true);
                                 })
                        // A blocked request waits on the queue's own token as well as the test's, because that is
                        // what a real DeviceRequest does: the queue cancels its token on disposal and the operation
                        // it wraps stops there. Waiting only on the test's token would model a request that ignores
                        // cancellation, which is the one shape the disposal drain must not be tuned against.
                       .Returns(async (CancellationToken queueCancellationToken, TimeSpan? _) =>
                                {
                                    if (shouldBlock)
                                    {
                                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, queueCancellationToken);
                                        await Task.Delay(-1, linked.Token);
                                    }

                                    if (shouldThrow)
                                    {
                                        throw new Exception();
                                    }
                                });
            requestMock.Setup(request => request.HandleRequestFailed(It.IsAny<Exception>()))
                       .Callback((Exception exception) =>
                                 {
                                     if (exception is RequestDroppedException droppedException)
                                     {
                                         _requestDroppedExceptions.Add(droppedException);
                                     }
                                 });

            return requestMock.Object;
        }
    }
}