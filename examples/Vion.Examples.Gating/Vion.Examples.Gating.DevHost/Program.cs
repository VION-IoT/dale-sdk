using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web;

namespace Vion.Examples.Gating.DevHost
{
    public class Program
    {
        private const int Port = 5000;

        public static Task Main(string[] args)
        {
            // Folder-driven boot (RFC 0008, topology-as-data): topologies/*.topology.json defines the
            // instance graph — here a single station whose instantiationParameters set ChargePointCount, so
            // `dale dev` shows exactly that many charge-point services live. Register block types in
            // DependencyInjection.cs. See the README for what to watch for.
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