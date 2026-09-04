using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     A request that operates on the client itself rather than on a device — today, only <c>Disconnect</c>. It
    ///     carries no receipt, does not feed the link accumulator, and is never expired by the queued-age check: a
    ///     disconnect that has waited a long time is exactly the one still worth doing.
    /// </summary>
    internal class ControlRequest : Request
    {
        private readonly Action<Exception>? _errorCallback;

        private readonly Func<CancellationToken, Task> _operation;

        private readonly Action? _successCallback;

        public ControlRequest(string requestName,
                              IActorDispatcher dispatcher,
                              Func<CancellationToken, Task> operation,
                              Action? successCallback,
                              Action<Exception>? errorCallback,
                              ILogger logger) : base(requestName, dispatcher, logger)
        {
            _operation = operation;
            _successCallback = successCallback;
            _errorCallback = errorCallback;
        }

        /// <inheritdoc />
        public override async Task ExecuteAsync(CancellationToken cancellationToken, TimeSpan? maxQueuedAge)
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

        /// <inheritdoc />
        public override void HandleRequestFailed(Exception exception)
        {
            // A control operation dropped on disposal is shutdown, not a fault; at Error every Modbus block's
            // teardown would put a line through the gateway's log pipeline. Same arm as DeviceRequest's.
            var outcome = ModbusOutcomeClassifier.Classify(exception);
            if (outcome is ModbusOutcome.BackedOff or ModbusOutcome.Expired or ModbusOutcome.Dropped or ModbusOutcome.Cancelled)
            {
                LogRequestNotExecuted(Name, outcome, Id, exception);
            }
            else
            {
                LogRequestFailed(Name, Id, exception);
            }

            if (_errorCallback == null)
            {
                return;
            }

            TryInvokeCallback(() => _errorCallback(exception));
        }
    }
}