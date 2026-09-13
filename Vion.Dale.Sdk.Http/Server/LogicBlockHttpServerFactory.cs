using System;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <inheritdoc />
    internal class LogicBlockHttpServerFactory : ILogicBlockHttpServerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public LogicBlockHttpServerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public ILogicBlockHttpServer Create()
        {
            return _serviceProvider.GetRequiredService<ILogicBlockHttpServer>();
        }
    }
}