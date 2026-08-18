using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class RequestFactory : IRequestFactory
    {
        private readonly TimeProvider _timeProvider;

        public RequestFactory(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public IRequest Create<T>(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T[]>> operation,
                                  Action<T[], ModbusReceipt> successCallback,
                                  Action<Exception, ModbusReceipt>? errorCallback,
                                  ModbusLinkAccumulator accumulator,
                                  ILogger logger)
            where T : unmanaged
        {
            return new ArrayResultRequest<T>(requestName,
                                             dispatcher,
                                             operation,
                                             successCallback,
                                             errorCallback,
                                             _timeProvider,
                                             accumulator,
                                             logger);
        }

        /// <inheritdoc />
        public IRequest Create<T>(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T>> operation,
                                  Action<T, ModbusReceipt> successCallback,
                                  Action<Exception, ModbusReceipt>? errorCallback,
                                  ModbusLinkAccumulator accumulator,
                                  ILogger logger)
        {
            return new SingleResultRequest<T>(requestName,
                                              dispatcher,
                                              operation,
                                              successCallback,
                                              errorCallback,
                                              _timeProvider,
                                              accumulator,
                                              logger);
        }

        /// <inheritdoc />
        public IRequest Create(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task> operation,
                               Action<ModbusReceipt>? successCallback,
                               Action<Exception, ModbusReceipt>? errorCallback,
                               ModbusLinkAccumulator accumulator,
                               ILogger logger)
        {
            return new VoidResultRequest(requestName,
                                         dispatcher,
                                         operation,
                                         successCallback,
                                         errorCallback,
                                         _timeProvider,
                                         accumulator,
                                         logger);
        }

        /// <inheritdoc />
        public IRequest CreateControlOperation(string requestName,
                                               IActorDispatcher dispatcher,
                                               Func<CancellationToken, Task> operation,
                                               Action? successCallback,
                                               Action<Exception>? errorCallback,
                                               ILogger logger)
        {
            return new ControlRequest(requestName,
                                      dispatcher,
                                      operation,
                                      successCallback,
                                      errorCallback,
                                      logger);
        }
    }
}