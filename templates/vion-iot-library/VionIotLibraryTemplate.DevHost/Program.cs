using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;

namespace VionIotLibraryTemplate.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            // Build a configuration for testing with fluent API
            var config = DevConfigurationBuilder.Create().AddLogicBlock<HelloWorld>().AddLogicBlock<SmartLedController>("SmartLed").Build();

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
            return DevHostWebRunner.RunAsync(host, 5000, cts.Token);
        }
    }
}