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
            // Folder-driven boot (RFC 0008, topology-as-data): the first topology under topologies/
            // (preferring "default") is loaded automatically. If none exists, default.topology.json is
            // generated from the DI catalog, announced on the console, and used. Add blocks in
            // DependencyInjection.cs and describe the instance graph in topologies/*.topology.json — the
            // topology-switch UI then works with no changes here. Interactive runs open the browser;
            // headless (DALE_DEVHOST_NO_BROWSER=1 — `dale dev --headless`, CI, an agent) print a JSON
            // readiness line instead. `dale dev --stepped` boots a deterministic virtual clock so scenarios
            // step exactly. See RFC 0003 (headless) and the scenario-authoring cookbook.
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