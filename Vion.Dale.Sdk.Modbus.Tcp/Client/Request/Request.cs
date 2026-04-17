using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal abstract partial class Request : IRequest
    {
        private readonly IActorDispatcher _dispatcher;

        private readonly Action<Exception>? _errorCallback;

        private readonly ILogger _logger;

        protected Request(string requestName, IActorDispatcher dispatcher, Action<Exception>? errorCallback, ILogger logger)
        {
            Name = requestName;
            _dispatcher = dispatcher;
            _errorCallback = errorCallback;
            _logger = logger;
        }

        /// <inheritdoc />
        public Guid Id { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public abstract Task ExecuteAsync(CancellationToken cancellationToken);

        /// <inheritdoc />
        public void HandleRequestFailed(Exception exception)
        {
            LogRequestFailed(Name, Id, exception);
            if (_errorCallback == null)
            {
                return;
            }

            TryInvokeCallback(() => _errorCallback(exception));
        }

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
        private partial void LogRequestFailed(string requestName, Guid requestId, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to invoke callback for request '{RequestName}' [{RequestId}]")]
        private partial void LogCallbackInvocationFailed(string requestName, Guid requestId, Exception exception);
    }
}