using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;
using Vion.Examples.ModbusRtu.LogicBlocks;

namespace Vion.Examples.ModbusRtu
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<Em122ElectricityMeter>();
            services.AddTransient<ModbusThroughputTest>();
        }
    }
}