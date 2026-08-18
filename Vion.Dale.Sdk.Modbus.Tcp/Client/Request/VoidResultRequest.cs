using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class VoidResultRequest : DeviceRequest
    {
        private readonly Func<CancellationToken, Task> _operation;

        private readonly Action<ModbusReceipt>? _successCallback;

        public VoidResultRequest(string requestName,
                                 IActorDispatcher dispatcher,
                                 Func<CancellationToken, Task> operation,
                                 Action<ModbusReceipt>? successCallback,
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
                await _operation(cancellationToken).ConfigureAwait(false);
                var receipt = CompleteSuccessfully(startedAt, queuedWait);
                if (_successCallback != null)
                {
                    TryInvokeCallback(() => _successCallback(receipt));
                }
            }
            catch (Exception exception)
            {
                HandleOperationFailed(exception, startedAt, queuedWait);
            }
        }
    }
}