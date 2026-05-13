using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;
using Microsoft.Extensions.DependencyInjection;
using Vion.Examples.Energy.LogicBlocks;
using Vion.Examples.Energy.Services;

namespace Vion.Examples.Energy
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Register logic blocks
            services.AddTransient<PhotovoltaicsSimulation>();
            services.AddTransient<HouseSimulation>();
            services.AddTransient<ChargingStationSimulation>();
            services.AddTransient<ChargingStationMultiPointSimulation>();
            services.AddTransient<BatterySimulation>();
            services.AddTransient<EnergyManagerSimulation>();
            services.AddTransient<WeatherDataService>();

            // Register services
            services.AddTransient<IMeteoService, OpenMeteoService>();
            services.AddTransient<IGeolocationService, GeolocationService>();

            // Register non-blocking HttpClient for logic blocks
            services.AddDaleHttpSdk();
        }
    }
}