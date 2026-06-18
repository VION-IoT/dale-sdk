using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web;

namespace Vion.Diagnostics.DevHost
{
    public class Program
    {
        private const int Port = 5000;

        public static Task Main(string[] args)
        {
            // Folder-driven boot (RFC 0008, topology-as-data): topologies/*.topology.json defines the
            // instance graph, discovered from disk — register block types in DependencyInjection.cs.
            // The diagnostics block reports on every actor the DevHost runtime spawns, including itself,
            // so its output reflects the live runtime (not a deterministic scenario surface).
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };
            return DevHostWebRunner.RunFolderDrivenAsync(b => b.WithDi<DependencyInjection>()
                                                               .WithWebUi()
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