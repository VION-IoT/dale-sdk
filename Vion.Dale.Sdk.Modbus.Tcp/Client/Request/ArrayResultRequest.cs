using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class ArrayResultRequest<T> : DeviceRequest
        where T : unmanaged
    {
        private readonly Func<CancellationToken, Task<T[]>> _operation;

        private readonly Action<T[], ModbusReceipt> _successCallback;

        public ArrayResultRequest(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T[]>> operation,
                                  Action<T[], ModbusReceipt> successCallback,
                                  Action<Exception, ModbusReceipt>? errorCallback,
                                  TimeProvider timeProvider,
                                  ModbusLinkAccumulator accumulator,
                                  ILogger logger) : base(requestName,
                                                         dispatcher,
                                                         errorCallback,
                                                         timeProvider,
                                                         accumulator,
                                                         logger)
        {
            _operation = operation;
            _successCallback = successCallback;
        }

        /// <inheritdoc />
        protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken, TimeSpan queuedWait)
        {
            var startedAt = StartOperation();
            try
            {
                var result = await _operation(cancellationToken).ConfigureAwait(false);
                var receipt = CompleteSuccessfully(startedAt, queuedWait);
                TryInvokeCallback(() => _successCallback(result, receipt));
            }
            catch (Exception exception)
            {
                HandleOperationFailed(exception, startedAt, queuedWait);
            }
        }
    }
}