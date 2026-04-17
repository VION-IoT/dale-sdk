using System;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <inheritdoc />
    public class LogicBlockModbusTcpClientFactory : ILogicBlockModbusTcpClientFactory
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LogicBlockModbusTcpClientFactory" /> class.
        /// </summary>
        /// <param name="serviceProvider">The service provider used to resolve <see cref="ILogicBlockModbusTcpClient" /> instances.</param>
        public LogicBlockModbusTcpClientFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public ILogicBlockModbusTcpClient Create()
        {
            return _serviceProvider.GetRequiredService<ILogicBlockModbusTcpClient>();
        }
    }
}