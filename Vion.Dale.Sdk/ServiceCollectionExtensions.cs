using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Examples.LogicBlocks;

namespace Vion.Dale.Sdk
{
    public static class ServiceCollectionExtensions
    {
        public static void AddDaleSdk(this IServiceCollection serviceCollection)
        {
            // Register non-generic ILogger so LogicBlockBase(ILogger) constructors can be resolved by DI
            serviceCollection.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger("Dale"));

            // logic blocks
            serviceCollection.AddTransient<ChargingStationMultiPointSimulation>();

            // TimeProvider.System is the real wall clock; tests swap it for a FakeTimeProvider via TestKit.
            serviceCollection.AddSingleton(TimeProvider.System);
        }
    }
}
