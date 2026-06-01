using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;
using Microsoft.Extensions.Logging;
using Vion.Examples.ModbusRtu.LogicBlocks;

namespace Vion.Examples.ModbusRtu.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<Em122ElectricityMeter>("EM122").AddLogicBlock<ModbusThroughputTest>("Durchsatztest").Build();

            var host = DevHostBuilder.Create()
                                     .WithDi<DependencyInjection>()
                                     .ConfigureServices(services => new Vion.Dale.Sdk.Modbus.Rtu.DependencyInjection().ConfigureServices(services))
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