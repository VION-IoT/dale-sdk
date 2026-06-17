using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.SmokeHost
{
    /// <summary>
    ///     Boots the smoke-host DevHost: folder-driven discovery of <c>./topologies</c> + <c>./scenarios</c>
    ///     (relative to the working directory), web UI + API on port 5000. Run with
    ///     <c>DALE_DEVHOST_STEPPED=1</c> for the deterministic clock and <c>DALE_DEVHOST_NO_BROWSER=1</c> for
    ///     headless agent/CI use. This is the boot target for the /devhost-smoke skill's live-UI tier.
    /// </summary>
    public static class Program
    {
        public static Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          cts.Cancel();
                                      };

            return DevHostWebRunner.RunFolderDrivenAsync(builder => builder.WithDi<DependencyInjection>()
                                                                           .WithWebUi()
                                                                           .ConfigureLogging(logging =>
                                                                                             {
                                                                                                 logging.AddConsole();
                                                                                                 logging.SetMinimumLevel(LogLevel.Information);
                                                                                             }),
                                                         5000,
                                                         cts.Token);
        }
    }
}