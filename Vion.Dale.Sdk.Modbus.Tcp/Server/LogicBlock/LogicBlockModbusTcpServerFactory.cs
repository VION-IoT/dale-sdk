using System;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    /// <inheritdoc />
    public class LogicBlockModbusTcpServerFactory : ILogicBlockModbusTcpServerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LogicBlockModbusTcpServerFactory" /> class.
        /// </summary>
        /// <param name="serviceProvider">The service provider used to resolve <see cref="ILogicBlockModbusTcpServer" /> instances.</param>
        public LogicBlockModbusTcpServerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public ILogicBlockModbusTcpServer Create()
        {
            return _serviceProvider.GetRequiredService<ILogicBlockModbusTcpServer>();
        }
    }
}