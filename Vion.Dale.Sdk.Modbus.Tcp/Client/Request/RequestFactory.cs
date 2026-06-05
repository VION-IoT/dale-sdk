using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    internal class RequestFactory : IRequestFactory
    {
        /// <inheritdoc />
        public IRequest Create<T>(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T[]>> operation,
                                  Action<T[]> successCallback,
                                  Action<Exception>? errorCallback,
                                  ILogger logger)
            where T : unmanaged
        {
            return new ArrayResultRequest<T>(requestName,
                                             dispatcher,
                                             operation,
                                             successCallback,
                                             errorCallback,
                                             logger);
        }

        /// <inheritdoc />
        public IRequest Create<T>(string requestName,
                                  IActorDispatcher dispatcher,
                                  Func<CancellationToken, Task<T>> operation,
                                  Action<T> successCallback,
                                  Action<Exception>? errorCallback,
                                  ILogger logger)
        {
            return new SingleResultRequest<T>(requestName,
                                              dispatcher,
                                              operation,
                                              successCallback,
                                              errorCallback,
                                              logger);
        }

        /// <inheritdoc />
        public IRequest Create(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task> operation,
                               Action? successCallback,
                               Action<Exception>? errorCallback,
                               ILogger logger)
        {
            return new VoidResultRequest(requestName,
                                         dispatcher,
                                         operation,
                                         successCallback,
                                         errorCallback,
                                         logger);
        }
    }
}