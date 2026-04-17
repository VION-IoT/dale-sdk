using System;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Modbus;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Factory for creating Modbus RTU read and write requests.
    /// </summary>
    internal partial class ModbusRtuRequestFactory : IModbusRtuRequestFactory
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger<ModbusRtuRequestFactory> _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusRtuRequestFactory" /> class.
        /// </summary>
        /// <param name="dateTimeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger for logging.</param>
        public ModbusRtuRequestFactory(IDateTimeProvider dateTimeProvider, ILogger<ModbusRtuRequestFactory> logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                         int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         TimeSpan operationTimeout,
                                                         Func<Memory<byte>, T[]> processResponse,
                                                         Action<T[]> successCallback,
                                                         Action<Exception>? errorCallback)
        {
            var correlationId = Guid.NewGuid();
            var createdAt = _dateTimeProvider.UtcNow;
            var expiresAt = _dateTimeProvider.Add(createdAt, operationTimeout);
            var readRequest = new ReadModbusRtuRequest(functionCode,
                                                       (byte)unitIdentifier,
                                                       startingAddress,
                                                       quantity,
                                                       createdAt,
                                                       expiresAt,
                                                       correlationId,
                                                       (data, exception) =>
                                                       {
                                                           T[] result;
                                                           try
                                                           {
                                                               if (exception == null)
                                                               {
                                                                   result = processResponse(data);
                                                               }
                                                               else
                                                               {
                                                                   HandleRequestFailed(errorCallback,
                                                                                       exception,
                                                                                       functionCode,
                                                                                       unitIdentifier,
                                                                                       startingAddress,
                                                                                       quantity,
                                                                                       correlationId);
                                                                   return;
                                                               }
                                                           }
                                                           catch (Exception responseProcessingException)
                                                           {
                                                               HandleRequestFailed(errorCallback,
                                                                                   responseProcessingException,
                                                                                   functionCode,
                                                                                   unitIdentifier,
                                                                                   startingAddress,
                                                                                   quantity,
                                                                                   correlationId);
                                                               return;
                                                           }

                                                           LogRequestSucceeded(functionCode, unitIdentifier, startingAddress, quantity, correlationId);
                                                           successCallback(result);
                                                       });

            return readRequest;
        }

        /// <inheritdoc />
        public ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                         int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         TimeSpan operationTimeout,
                                                         Func<Memory<byte>, T> processResponse,
                                                         Action<T> successCallback,
                                                         Action<Exception>? errorCallback)
        {
            var correlationId = Guid.NewGuid();
            var createdAt = _dateTimeProvider.UtcNow;
            var expiresAt = _dateTimeProvider.Add(createdAt, operationTimeout);
            var readRequest = new ReadModbusRtuRequest(functionCode,
                                                       (byte)unitIdentifier,
                                                       startingAddress,
                                                       quantity,
                                                       createdAt,
                                                       expiresAt,
                                                       correlationId,
                                                       (data, exception) =>
                                                       {
                                                           T result;
                                                           try
                                                           {
                                                               if (exception == null)
                                                               {
                                                                   result = processResponse(data);
                                                               }
                                                               else
                                                               {
                                                                   HandleRequestFailed(errorCallback,
                                                                                       exception,
                                                                                       functionCode,
                                                                                       unitIdentifier,
                                                                                       startingAddress,
                                                                                       quantity,
                                                                                       correlationId);
                                                                   return;
                                                               }
                                                           }
                                                           catch (Exception responseProcessingException)
                                                           {
                                                               HandleRequestFailed(errorCallback,
                                                                                   responseProcessingException,
                                                                                   functionCode,
                                                                                   unitIdentifier,
                                                                                   startingAddress,
                                                                                   quantity,
                                                                                   correlationId);
                                                               return;
                                                           }

                                                           LogRequestSucceeded(functionCode, unitIdentifier, startingAddress, quantity, correlationId);
                                                           successCallback(result);
                                                       });

            return readRequest;
        }

        /// <inheritdoc />
        public WriteModbusRtuRequest CreateWriteRequest(ModbusFunctionCode functionCode,
                                                        int unitIdentifier,
                                                        ushort address,
                                                        byte[] data,
                                                        TimeSpan operationTimeout,
                                                        Action? successCallback,
                                                        Action<Exception>? errorCallback)
        {
            var correlationId = Guid.NewGuid();
            var createdAt = _dateTimeProvider.UtcNow;
            var expiresAt = _dateTimeProvider.Add(createdAt, operationTimeout);
            var writeRequest = new WriteModbusRtuRequest(functionCode,
                                                         (byte)unitIdentifier,
                                                         address,
                                                         data,
                                                         createdAt,
                                                         expiresAt,
                                                         correlationId,
                                                         exception =>
                                                         {
                                                             if (exception == null)
                                                             {
                                                                 LogRequestSucceeded(functionCode, unitIdentifier, address, correlationId);
                                                                 successCallback?.Invoke();
                                                             }
                                                             else
                                                             {
                                                                 LogRequestFailed(functionCode, unitIdentifier, address, correlationId, exception);
                                                                 errorCallback?.Invoke(exception);
                                                             }
                                                         });

            return writeRequest;
        }

        private void HandleRequestFailed(Action<Exception>? errorCallback,
                                         Exception exception,
                                         ModbusFunctionCode functionCode,
                                         int unitIdentifier,
                                         ushort address,
                                         ushort quantity,
                                         Guid correlationId)
        {
            LogRequestFailed(functionCode,
                             unitIdentifier,
                             address,
                             quantity,
                             correlationId,
                             exception);
            errorCallback?.Invoke(exception);
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

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Request succeeded (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, CorrelationId={CorrelationId})")]
        partial void LogRequestSucceeded(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Error,
                       Message = "Request failed (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, CorrelationId={CorrelationId})")]
        partial void LogRequestFailed(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId, Exception exception);
    }
}