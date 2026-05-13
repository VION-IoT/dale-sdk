using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;
using Vion.Examples.Presentation.LogicBlocks;

namespace Vion.Examples.Presentation.DevHost
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<PresentationDemo>().AutoConnect().Build();

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

            OpenBrowser();

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
