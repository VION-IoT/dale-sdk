using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.SmokeHost.LogicBlocks;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.SmokeHost
{
    /// <summary>The block catalog the DevHost instantiates — discovered via <c>WithDi&lt;DependencyInjection&gt;()</c>.</summary>
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<ShowcaseBlock>();
            serviceCollection.AddTransient<IoBlock>();
            serviceCollection.AddTransient<GridBlock>();
            serviceCollection.AddTransient<SignalSourceBlock>();
            serviceCollection.AddTransient<SignalSinkBlock>();
            serviceCollection.AddTransient<GatedStationBlock>();
        }
    }
}