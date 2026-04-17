using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;
using Microsoft.Extensions.Logging;

namespace VionIotLibraryTemplate.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            // Build a configuration for testing with fluent API
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<HelloWorld>()
                                                .AddLogicBlock<SmartLedController>("SmartLed")
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

            // Open browser automatically
            OpenBrowser();

            // wait for Ctrl+C
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };
            return host.RunAsync(cts.Token);
        }

        private static void OpenBrowser()
        {
            var url = "http://localhost:5000";
            Console.WriteLine($"Opening browser at {url}...");

            try
            {
                Process.Start(new ProcessStartInfo
                              {
                                  FileName = url,
                                  UseShellExecute = true,
                              });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not open browser: {ex.Message}");
                Console.WriteLine($"Please navigate to {url} manually.");
            }
        }
    }
}