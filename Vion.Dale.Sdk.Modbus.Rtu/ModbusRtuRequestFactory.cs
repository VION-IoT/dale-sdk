using System;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Factory for creating Modbus RTU read and write requests.
    /// </summary>
    /// <remarks>
    ///     The callback each request carries is the block side of the transaction: it runs on the contract's actor when
    ///     the response comes back, records the receipt the handler stamped, and hands result and receipt to the caller
    ///     through the caller's own dispatcher — the same hop Modbus TCP makes.
    /// </remarks>
    internal partial class ModbusRtuRequestFactory : IModbusRtuRequestFactory
    {
        private readonly ILogger<ModbusRtuRequestFactory> _logger;

        private readonly TimeProvider _timeProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusRtuRequestFactory" /> class.
        /// </summary>
        /// <param name="timeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger for logging.</param>
        public ModbusRtuRequestFactory(TimeProvider timeProvider, ILogger<ModbusRtuRequestFactory> logger)
        {
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                         int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         TimeSpan operationTimeout,
                                                         TimeSpan? maxQueuedAge,
                                                         Func<Memory<byte>, T[]> processResponse,
                                                         IActorDispatcher dispatcher,
                                                         Action<T[], ModbusReceipt> successCallback,
                                                         Action<Exception, ModbusReceipt>? errorCallback,
                                                         ModbusLinkAccumulator accumulator)
        {
            var correlationId = Guid.NewGuid();

            return new ReadModbusRtuRequest(functionCode,
                                            (byte)unitIdentifier,
                                            startingAddress,
                                            quantity,
                                            _timeProvider.GetUtcNow().UtcDateTime,
                                            operationTimeout,
                                            maxQueuedAge,
                                            correlationId,
                                            (data, exception, receipt) => CompleteRead(data,
                                                                                       exception,
                                                                                       receipt,
                                                                                       processResponse,
                                                                                       dispatcher,
                                                                                       successCallback,
                                                                                       errorCallback,
                                                                                       accumulator,
                                                                                       functionCode,
                                                                                       unitIdentifier,
                                                                                       startingAddress,
                                                                                       quantity,
                                                                                       correlationId));
        }

        /// <inheritdoc />
        public ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                         int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         TimeSpan operationTimeout,
                                                         TimeSpan? maxQueuedAge,
                                                         Func<Memory<byte>, T> processResponse,
                                                         IActorDispatcher dispatcher,
                                                         Action<T, ModbusReceipt> successCallback,
                                                         Action<Exception, ModbusReceipt>? errorCallback,
                                                         ModbusLinkAccumulator accumulator)
        {
            var correlationId = Guid.NewGuid();

            return new ReadModbusRtuRequest(functionCode,
                                            (byte)unitIdentifier,
                                            startingAddress,
                                            quantity,
                                            _timeProvider.GetUtcNow().UtcDateTime,
                                            operationTimeout,
                                            maxQueuedAge,
                                            correlationId,
                                            (data, exception, receipt) => CompleteRead(data,
                                                                                       exception,
                                                                                       receipt,
                                                                                       processResponse,
                                                                                       dispatcher,
                                                                                       successCallback,
                                                                                       errorCallback,
                                                                                       accumulator,
                                                                                       functionCode,
                                                                                       unitIdentifier,
                                                                                       startingAddress,
                                                                                       quantity,
                                                                                       correlationId));
        }

        /// <inheritdoc />
        public WriteModbusRtuRequest CreateWriteRequest(ModbusFunctionCode functionCode,
                                                        int unitIdentifier,
                                                        ushort address,
                                                        byte[] data,
                                                        TimeSpan operationTimeout,
                                                        TimeSpan? maxQueuedAge,
                                                        IActorDispatcher dispatcher,
                                                        Action<ModbusReceipt>? successCallback,
                                                        Action<Exception, ModbusReceipt>? errorCallback,
                                                        ModbusLinkAccumulator accumulator)
        {
            var correlationId = Guid.NewGuid();

            return new WriteModbusRtuRequest(functionCode,
                                             (byte)unitIdentifier,
                                             address,
                                             data,
                                             _timeProvider.GetUtcNow().UtcDateTime,
                                             operationTimeout,
                                             maxQueuedAge,
                                             correlationId,
                                             (exception, receipt) => CompleteWrite(exception,
                                                                                   receipt,
                                                                                   dispatcher,
                                                                                   successCallback,
                                                                                   errorCallback,
                                                                                   accumulator,
                                                                                   functionCode,
                                                                                   unitIdentifier,
                                                                                   address,
                                                                                   correlationId));
        }

        private void CompleteWrite(Exception? exception,
                                   ModbusReceipt receipt,
                                   IActorDispatcher dispatcher,
                                   Action<ModbusReceipt>? successCallback,
                                   Action<Exception, ModbusReceipt>? errorCallback,
                                   ModbusLinkAccumulator accumulator,
                                   ModbusFunctionCode functionCode,
                                   int unitIdentifier,
                                   ushort address,
                                   Guid correlationId)
        {
            accumulator.Record(receipt);
            if (exception == null)
            {
                LogRequestSucceeded(functionCode, unitIdentifier, address, correlationId);
                if (successCallback != null)
                {
                    dispatcher.InvokeSynchronized(() => successCallback(receipt));
                }

                return;
            }

            if (receipt.Outcome is ModbusOutcome.Expired or ModbusOutcome.Dropped)
            {
                LogRequestNotExecuted(functionCode,
                                      unitIdentifier,
                                      address,
                                      receipt.Outcome,
                                      correlationId,
                                      exception);
            }
            else
            {
                LogRequestFailed(functionCode, unitIdentifier, address, correlationId, exception);
            }

            if (errorCallback != null)
            {
                dispatcher.InvokeSynchronized(() => errorCallback(exception, receipt));
            }
        }

        private void CompleteRead<TResult>(byte[]? data,
                                           Exception? exception,
                                           ModbusReceipt receipt,
                                           Func<Memory<byte>, TResult> processResponse,
                                           IActorDispatcher dispatcher,
                                           Action<TResult, ModbusReceipt> successCallback,
                                           Action<Exception, ModbusReceipt>? errorCallback,
                                           ModbusLinkAccumulator accumulator,
                                           ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           ushort quantity,
                                           Guid correlationId)
        {
            if (exception != null)
            {
                HandleRequestFailed(receipt,
                                    exception,
                                    dispatcher,
                                    errorCallback,
                                    accumulator,
                                    functionCode,
                                    unitIdentifier,
                                    startingAddress,
                                    quantity,
                                    correlationId);

                return;
            }

            TResult result;
            try
            {
                result = processResponse(data);
            }
            catch (Exception responseProcessingException)
            {
                // The device answered; this is our own reading of the answer failing, so the handler's Success
                // must be corrected before it reaches the link summary.
                var failed = receipt with { Outcome = ModbusRtuOutcomeClassifier.ClassifyResponseFailure(responseProcessingException) };
                HandleRequestFailed(failed,
                                    responseProcessingException,
                                    dispatcher,
                                    errorCallback,
                                    accumulator,
                                    functionCode,
                                    unitIdentifier,
                                    startingAddress,
                                    quantity,
                                    correlationId);

                return;
            }

            accumulator.Record(receipt);
            LogRequestSucceeded(functionCode, unitIdentifier, startingAddress, quantity, correlationId);
            dispatcher.InvokeSynchronized(() => successCallback(result, receipt));
        }

        private void HandleRequestFailed(ModbusReceipt receipt,
                                         Exception exception,
                                         IActorDispatcher dispatcher,
                                         Action<Exception, ModbusReceipt>? errorCallback,
                                         ModbusLinkAccumulator accumulator,
                                         ModbusFunctionCode functionCode,
                                         int unitIdentifier,
                                         ushort address,
                                         ushort quantity,
                                         Guid correlationId)
        {
            accumulator.Record(receipt);
            if (receipt.Outcome is ModbusOutcome.Expired or ModbusOutcome.Dropped)
            {
                LogRequestNotExecuted(functionCode,
                                      unitIdentifier,
                                      address,
                                      quantity,
                                      receipt.Outcome,
                                      correlationId,
                                      exception);
            }
            else
            {
                LogRequestFailed(functionCode,
                                 unitIdentifier,
                                 address,
                                 quantity,
                                 correlationId,
                                 exception);
            }

            if (errorCallback != null)
            {
                dispatcher.InvokeSynchronized(() => errorCallback(exception, receipt));
            }
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Request succeeded (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Quantity={Quantity}, " +
                                 "CorrelationId={CorrelationId})")]
        partial void LogRequestSucceeded(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, ushort quantity, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Error,
                       Message = "Request failed (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Quantity={Quantity}, " +
                                 "CorrelationId={CorrelationId})")]
        partial void LogRequestFailed(ModbusFunctionCode functionCode,
                                      int unitIdentifier,
                                      ushort address,
                                      ushort quantity,
                                      Guid correlationId,
                                      Exception exception);

        // Congestion is expected under load; at Error a single overload turns into a flood in the gateway's log
        // pipeline. The outcome on the receipt is what a block reacts to.
        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Request was not executed (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Quantity={Quantity}, " +
                                 "Outcome={Outcome}, CorrelationId={CorrelationId})")]
        partial void LogRequestNotExecuted(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort address,
                                           ushort quantity,
                                           ModbusOutcome outcome,
                                           Guid correlationId,
                                           Exception exception);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Request succeeded (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, CorrelationId={CorrelationId})")]
        partial void LogRequestSucceeded(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Error,
                       Message = "Request failed (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, CorrelationId={CorrelationId})")]
        partial void LogRequestFailed(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Request was not executed (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Outcome={Outcome}, " +
                                 "CorrelationId={CorrelationId})")]
        partial void LogRequestNotExecuted(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort address,
                                           ModbusOutcome outcome,
                                           Guid correlationId,
                                           Exception exception);
    }
}