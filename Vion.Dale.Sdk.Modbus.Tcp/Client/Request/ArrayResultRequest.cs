using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class ArrayResultRequest<T> : Request
        where T : unmanaged
    {
        private readonly Func<CancellationToken, Task<T[]>> _operation;

        private readonly Action<T[]> _successCallback;

        public ArrayResultRequest(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T[]>> operation,
                                  Action<T[]> successCallback,
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
                var result = await _operation(cancellationToken).ConfigureAwait(false);
                LogRequestSucceeded(Name, Id);
                TryInvokeCallback(() => _successCallback(result));
            }
            catch (Exception exception)
            {
                HandleRequestFailed(exception);
            }
        }
    }
}