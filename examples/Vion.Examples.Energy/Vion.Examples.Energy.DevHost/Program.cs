using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.LogicBlocks;

namespace Vion.Examples.Energy.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            // Build a configuration for testing with fluent API
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<EnergyManagerSimulation>("EnergyManager", out var ems)
                                                .AddLogicBlock<PhotovoltaicsSimulation>("PV", out var pv)
                                                .AddLogicBlock<HouseSimulation>("House", out var house)
                                                .AddLogicBlock<ChargingStationSimulation>("ChargingStation", out var cs)
                                                .AddLogicBlock<ChargingStationMultiPointSimulation>("ChargingStationMultiPoint", out var csmp)
                                                .AddLogicBlock<BatterySimulation>("Battery", out var battery)
                                                .AddLogicBlock<WeatherDataService>("WeatherService", out var weather)
                                                .Connect(pv, ems)
                                                .Connect(house, ems)
                                                .Connect(cs, ems)
                                                .Connect(csmp, ems)
                                                .Connect(battery, ems)
                                                .Build();

            // Create, configure and run the dev host
            var host = DevHostBuilder.Create()
                                     .WithDi<DependencyInjection>()
                                     .WithConfiguration(config)
                                     .WithWebUi()
                                     .ConfigureLogging(logging =>
                                                       {
                                                           logging.AddConsole();
                                                           logging.SetMinimumLevel(LogLevel.Debug);
                                                       })
                                     .Build();

            // Start the host. Interactive: opens the browser. Headless (DALE_DEVHOST_NO_BROWSER=1 — e.g.
            // `dale dev --headless`, CI, or an agent): prints a JSON readiness line instead. See RFC 0003.
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };
            return DevHostWebRunner.RunAsync(host, port: 5000, cancellationToken: cts.Token);
        }
    }
}