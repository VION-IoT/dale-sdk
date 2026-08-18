using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal abstract partial class Request : IRequest
    {
        private readonly IActorDispatcher _dispatcher;

        private readonly ILogger _logger;

        protected Request(string requestName, IActorDispatcher dispatcher, ILogger logger)
        {
            Name = requestName;
            _dispatcher = dispatcher;
            _logger = logger;
        }

        /// <inheritdoc />
        public Guid Id { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public abstract Task ExecuteAsync(CancellationToken cancellationToken, TimeSpan? maxQueuedAge);

        /// <inheritdoc />
        public abstract void HandleRequestFailed(Exception exception);

        protected void TryInvokeCallback(Action callback)
        {
            try
            {
                _dispatcher.InvokeSynchronized(callback);
            }
            catch (Exception exception)
            {
                LogCallbackInvocationFailed(Name, Id, exception);
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Request '{RequestName}' succeeded [{RequestId}]")]
        protected partial void LogRequestSucceeded(string requestName, Guid requestId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Request '{RequestName}' failed [{RequestId}]")]
        protected partial void LogRequestFailed(string requestName, Guid requestId, Exception exception);

        // Congestion and disposal are expected under load and on shutdown; at Error they turn a single fault into a
        // flood in the gateway's log pipeline. The outcome on the receipt is what a block reacts to.
        [LoggerMessage(Level = LogLevel.Debug, Message = "Request '{RequestName}' was not executed ({Outcome}) [{RequestId}]")]
        protected partial void LogRequestNotExecuted(string requestName, ModbusOutcome outcome, Guid requestId, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to invoke callback for request '{RequestName}' [{RequestId}]")]
        private partial void LogCallbackInvocationFailed(string requestName, Guid requestId, Exception exception);
    }
}