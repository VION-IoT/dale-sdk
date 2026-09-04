using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal partial class RequestQueue : IRequestQueue
    {
        private readonly ILogger<RequestQueue> _logger;

        private readonly IRequestFactory _requestFactory;

        private ModbusLinkAccumulator? _accumulator;

        private Channel<IRequest>? _channel;

        // ReSharper disable once NotAccessedField.Local
        private Task? _consumer;

        private CancellationTokenSource? _cts;

        private bool _disposed;

        // Written on the actor thread, read on the consumer thread. Stored as ticks (with -1 for "no limit") so a
        // 32-bit gateway cannot observe a half-written TimeSpan? and silently apply a different policy.
        private long _maxQueuedAgeTicks = -1;

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
        public TimeSpan? MaxQueuedAge
        {
            get
            {
                var ticks = Interlocked.Read(ref _maxQueuedAgeTicks);

                return ticks < 0 ? null : TimeSpan.FromTicks(ticks);
            }

            set
            {
                // -1 ticks is this field's "no limit" sentinel, so a negative TimeSpan would read back as null
                // and silently mean the opposite of what was asked.
                if (value is { } age && age <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), age, $"{nameof(MaxQueuedAge)} must be greater than zero, or null to disable the check.");
                }

                Interlocked.Exchange(ref _maxQueuedAgeTicks, value?.Ticks ?? -1);
            }
        }

        /// <inheritdoc />
        public void Initialize(int capacity, QueueOverflowPolicy overflowPolicy, ModbusLinkAccumulator accumulator)
        {
            if (_channel != null)
            {
                throw new InvalidOperationException($"{nameof(RequestQueue)} is already initialized.");
            }

            _accumulator = accumulator;

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
                                                       request => { request.HandleRequestFailed(new RequestDroppedException(request.Name, capacity, overflowPolicy)); });
            LogQueueCreated(capacity, overflowPolicy);

            _cts = new CancellationTokenSource();
            _consumer = ConsumeAsync(_channel, _cts.Token);
        }

        /// <inheritdoc />
        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T[]>> operation,
                               Action<T[], ModbusReceipt> successCallback,
                               Action<Exception, ModbusReceipt>? errorCallback)
            where T : unmanaged
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 RequireAccumulator(),
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T>> operation,
                               Action<T, ModbusReceipt> successCallback,
                               Action<Exception, ModbusReceipt>? errorCallback)
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 RequireAccumulator(),
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void Enqueue(string requestName,
                            IActorDispatcher dispatcher,
                            Func<CancellationToken, Task> operation,
                            Action<ModbusReceipt>? successCallback,
                            Action<Exception, ModbusReceipt>? errorCallback)
        {
            var request = _requestFactory.Create(requestName,
                                                 dispatcher,
                                                 operation,
                                                 successCallback,
                                                 errorCallback,
                                                 RequireAccumulator(),
                                                 _logger);
            EnqueueCore(request);
        }

        /// <inheritdoc />
        public void EnqueueControlOperation(string requestName,
                                            IActorDispatcher dispatcher,
                                            Func<CancellationToken, Task> operation,
                                            Action? successCallback,
                                            Action<Exception>? errorCallback)
        {
            var request = _requestFactory.CreateControlOperation(requestName,
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

        private ModbusLinkAccumulator RequireAccumulator()
        {
            return _accumulator ?? throw new InvalidOperationException($"{nameof(RequestQueue)} is not initialized.");
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
                request.HandleRequestFailed(new RequestDroppedException(request.Name));
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
                await request.ExecuteAsync(token, MaxQueuedAge).ConfigureAwait(false);
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