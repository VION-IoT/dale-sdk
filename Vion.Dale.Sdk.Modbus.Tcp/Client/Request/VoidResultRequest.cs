using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class VoidResultRequest : Request
    {
        private readonly Func<CancellationToken, Task> _operation;

        private readonly Action? _successCallback;

        public VoidResultRequest(string requestName,
                                 IActorDispatcher dispatcher,
                                 Func<CancellationToken, Task> operation,
                                 Action? successCallback,
                                 Action<Exception>? errorCallback,
                                 ILogger logger) : base(requestName, dispatcher, errorCallback, logger)
        {
            _operation = operation;
            _successCallback = successCallback;
        }

        /// <inheritdoc />
        public override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _operation(cancellationToken).ConfigureAwait(false);
                LogRequestSucceeded(Name, Id);
                if (_successCallback != null)
                {
                    TryInvokeCallback(_successCallback);
                }
            }
            catch (Exception exception)
            {
                HandleRequestFailed(exception);
            }
        }
    }
}