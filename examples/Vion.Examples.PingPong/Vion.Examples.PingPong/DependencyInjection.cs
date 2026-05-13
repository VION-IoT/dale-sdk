using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;
using Vion.Examples.PingPong.LogicBlocks;

namespace Vion.Examples.PingPong
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<Ping>();
            services.AddTransient<Pong>();
        }
    }
}