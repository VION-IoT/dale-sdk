using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;
using Vion.Diagnostics.LogicBlocks;

namespace Vion.Diagnostics.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            // Run the diagnostics block on its own so its service surface (the LogicBlocks table,
            // Status pill, RuntimeHealth, and the tunable thresholds) can be inspected in the Web UI.
            // The block reports on every actor the DevHost runtime spawns — including itself.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<DiagnosticsCollector>().AutoConnect().Build();

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

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };
            return DevHostWebRunner.RunAsync(host, 5000, cts.Token);
        }
    }
}