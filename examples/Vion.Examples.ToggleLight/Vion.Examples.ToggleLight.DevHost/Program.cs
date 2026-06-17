using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web;

namespace Vion.Examples.ToggleLight.DevHost
{
    public class Program
    {
        private const int Port = 5000;

        public static Task Main(string[] args)
        {
            // Folder-driven boot (RFC 0008, topology-as-data): topologies/*.topology.json defines the
            // instance graph and scenarios/*.scenario.json the replayable checks — both discovered from
            // disk, so adding a block or rewiring is a topology edit, not a Program.cs change. Register
            // block types in DependencyInjection.cs. `dale dev --stepped` gives a deterministic virtual
            // clock for scenario runs. See the template's AGENTS.md and the scenario-authoring cookbook.
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