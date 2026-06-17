using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web;

namespace VionIotLibraryTemplate.DevHost
{
    public class Program
    {
        private const int Port = 5000;

        public static Task Main(string[] args)
        {
            // Folder-driven boot: if a topologies/ directory exists the first topology (preferring
            // "default") is loaded automatically. If none exists, default.topology.json is generated
            // from the DI catalog, announced on the console, and used. The topology-switch UI works
            // without any Program.cs changes. See RFC 0006.
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };
            return DevHostWebRunner.RunFolderDrivenAsync(
                b => b.WithDi<DependencyInjection>()
                      .WithWebUi(Port)
                      .ConfigureLogging(logging =>
                                        {
                                            logging.AddConsole();
                                            logging.SetMinimumLevel(LogLevel.Debug);
                                        }),
                Port,
                cts.Token);
        }
    }
}
