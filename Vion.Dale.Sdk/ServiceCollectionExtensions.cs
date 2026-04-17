using Vion.Dale.Sdk.Examples.LogicBlocks;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

            // other, services, etc.
            serviceCollection.AddTransient<IDateTimeProvider, DateTimeProvider>();
        }
    }
}