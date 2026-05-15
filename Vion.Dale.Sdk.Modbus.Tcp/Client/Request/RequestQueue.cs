using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal partial class RequestQueue : IRequestQueue
    {
        private readonly ILogger<RequestQueue> _logger;

        private readonly IRequestFactory _requestFactory;

        private Channel<IRequest>? _channel;

        // ReSharper disable once NotAccessedField.Local
        private Task? _consumer;

        private CancellationTokenSource? _cts;

        private bool _disposed;

        public RequestQueue(IRequestFactory requestFactory, ILogger<RequestQueue> logger)
        {
            _requestFactory = requestFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public int QueuedRequestCount
        {
            get => _channel?.Reader.Count ?? 0;
        }

        /// <inheritdoc />
        public void Initialize(int capacity, QueueOverflowPolicy overflowPolicy)
        {
            if (_channel != null)
            {
                throw new InvalidOperationException($"{nameof(RequestQueue)} is already initialized.");
            }

            var fullMode = overflowPolicy switch
            {
                QueueOverflowPolicy.DropNewest => BoundedChannelFullMode.DropNewest,
                QueueOverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
                QueueOverflowPolicy.RejectNew => BoundedChannelFullMode.DropWrite,
                _ => throw new NotSupportedException($"Overflow policy {overflowPolicy} is not supported."),
            };

            _channel = Channel.CreateBounded<IRequest>(new BoundedChannelOptions(capacity)
                                                       {
                                                           SingleReader = true,
                                                           SingleWriter = true,
                                                           FullMode = fullMode,
                                                           AllowSynchronousContinuations =
                                                               false, // Prevent synchronous continuations to avoid blocking the channel writer thread.
                                                       },
                                                       request => { request.HandleRequestFailed(new RequestDroppedException(request.Name, "queue full")); });
            LogQueueCreated(capacity, overflowPolicy);

            _cts = new CancellationTokenSource();
            _consumer = ConsumeAsync(_channel, _cts.Token);
        }

        /// <inheritdoc />
        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T[]>> operation,
                               Action<T[]> successCallback,
                               Action<Exception>? errorCallback)
            where T : unmanaged
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T>> operation,
                               Action<T> successCallback,
                               Action<Exception>? errorCallback)
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void Enqueue(string requestName, IActorDispatcher dispatcher, Func<CancellationToken, Task> operation, Action? successCallback, Action<Exception>? errorCallback)
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void EnqueueCore(IRequest request)
        {
            if (_channel == null)
            {
                throw new InvalidOperationException($"{nameof(RequestQueue)} is not initialized.");
            }

            LogEnqueuingRequest(request.Name, request.Id);
            if (_channel!.Writer.TryWrite(request))
            {
                LogRequestEnqueued(request.Name, request.Id);
            }
            else
            {
                // TryWrite only returns false if BoundedChannelFullMode.Wait were set and the channel were full, which is not supported here, or when the channel is completed.
                LogRequestDroppedChannelCompleted(request.Name, request.Id);
                request.HandleRequestFailed(new RequestDroppedException(request.Name, "queue disposed"));
            }
        }

        private async Task ConsumeAsync(Channel<IRequest> channel, CancellationToken token)
        {
            LogConsumerStarted();
            try
            {
                await foreach (var request in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await ProcessRequestAsync(request, token);
                }

                LogConsumerCompleted();
            }
            catch (Exception exception) when (exception is OperationCanceledException or TaskCanceledException)
            {
                LogConsumerStopped();
            }
            catch (Exception exception)
            {
                LogUnexpectedConsumerError(exception);
            }
        }

        private async Task ProcessRequestAsync(IRequest request, CancellationToken token)
        {
            LogProcessingRequest(request.Name, request.Id);
            try
            {
                await request.ExecuteAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogUnexpectedRequestError(request.Name, request.Id, exception);
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                LogDisposing(nameof(RequestQueue));
                _channel?.Writer.TryComplete();
                _cts?.Cancel();
                _cts?.Dispose();
                LogDisposed(nameof(RequestQueue));
            }

            _disposed = true;
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Request queue created with capacity {capacity} and overflow policy {overflowPolicy}")]
        private partial void LogQueueCreated(int capacity, QueueOverflowPolicy overflowPolicy);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Enqueuing request (RequestName={RequestName}, RequestId={RequestId})")]
        private partial void LogEnqueuingRequest(string requestName, Guid requestId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Request enqueued (RequestName={RequestName}, RequestId={RequestId})")]
        private partial void LogRequestEnqueued(string requestName, Guid requestId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Request dropped because channel is completed (RequestName={RequestName}, RequestId={RequestId})")]
        private partial void LogRequestDroppedChannelCompleted(string requestName, Guid requestId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Consumer started")]
        private partial void LogConsumerStarted();

        [LoggerMessage(Level = LogLevel.Information, Message = "Consumer completed")]
        private partial void LogConsumerCompleted();

        [LoggerMessage(Level = LogLevel.Information, Message = "Consumer stopped due to cancellation")]
        private partial void LogConsumerStopped();

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in consumer")]
        private partial void LogUnexpectedConsumerError(Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Processing request (RequestName={RequestName}, RequestId={RequestId})")]
        private partial void LogProcessingRequest(string requestName, Guid requestId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error during processing of request (RequestName={RequestName}, RequestId={RequestId})")]
        private partial void LogUnexpectedRequestError(string requestName, Guid requestId, Exception exception);

        [LoggerMessage(Level = LogLevel.Information, Message = "Disposing {name}")]
        private partial void LogDisposing(string name);

        [LoggerMessage(Level = LogLevel.Information, Message = "{name} disposed")]
        private partial void LogDisposed(string name);
    }
}