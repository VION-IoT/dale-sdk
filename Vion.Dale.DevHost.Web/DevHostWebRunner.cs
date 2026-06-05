using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Web
{
    /// <summary>
    ///     Runs a web-enabled DevHost from a <c>Program.Main</c>: starts the host, then either opens the
    ///     browser (interactive, the default) or — when the <c>DALE_DEVHOST_NO_BROWSER</c> environment
    ///     variable is set to <c>1</c> — stays headless and prints a machine-readable readiness line for
    ///     tools/agents (RFC 0003). Centralizes what each generated DevHost <c>Program.cs</c> used to do
    ///     by hand, so headless mode is consistent and the browser/readiness logic lives in one place.
    /// </summary>
    public static class DevHostWebRunner
    {
        /// <summary>The env var that switches a web DevHost into headless (no-browser) mode.</summary>
        public const string NoBrowserEnvVar = "DALE_DEVHOST_NO_BROWSER";

        /// <summary>
        ///     Starts <paramref name="host" />, signals readiness or opens the browser, and runs until
        ///     <paramref name="cancellationToken" /> is cancelled (e.g. Ctrl+C), then stops the host.
        /// </summary>
        /// <param name="host">The built DevHost (created with <c>.WithWebUi(port)</c>).</param>
        /// <param name="port">The port the web UI / API is served on (used for the browser URL and readiness line).</param>
        /// <param name="cancellationToken">Cancelled to shut down (typically wired to Ctrl+C).</param>
        public static async Task RunAsync(IDevHost host, int port = 5000, CancellationToken cancellationToken = default)
        {
            var headless = Environment.GetEnvironmentVariable(NoBrowserEnvVar) == "1";

            await host.StartAsync(cancellationToken);

            if (headless)
            {
                // Single-line, parseable readiness signal — lets an agent that spawned this process know
                // the network is up and on which port before it starts driving /api.
                Console.WriteLine($"{{\"ready\":true,\"port\":{port}}}");
            }
            else
            {
                OpenBrowser($"http://localhost:{port}");
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on Ctrl+C / cancellation.
            }

            await host.StopAsync(CancellationToken.None);
        }

        private static void OpenBrowser(string url)
        {
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