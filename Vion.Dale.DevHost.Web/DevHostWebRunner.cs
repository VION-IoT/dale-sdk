using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
        ///     One-shot export mode (RFC 0006 R4): when set to a file path, the runner boots the host, writes
        ///     the wired network's <c>ConfigurationOutput</c> JSON to that path (the same shape
        ///     <c>GET /api/configuration</c> serves — block instance names, service identifiers, schemas,
        ///     topology name), and exits. <c>dale scenario validate</c> / <c>schema</c> consume the export.
        /// </summary>
        public const string ExportConfigEnvVar = "DALE_DEVHOST_EXPORT_CONFIG";

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

            if (TryExportConfig(host))
            {
                await host.StopAsync(CancellationToken.None);
                return;
            }

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

        /// <summary>
        ///     Supervised variant: builds the host from <paramref name="hostFactory" /> and recycles it —
        ///     dispose, rebuild, restart on the same port — whenever the UI/API requests a reset
        ///     (<c>POST /api/control/reset</c> → <see cref="Control.IDevHostControl.TryRequestReset" />).
        ///     This kills the kill-and-`dale dev` loop: a code-independent fresh start without leaving the
        ///     browser. Runs until <paramref name="cancellationToken" /> is cancelled.
        /// </summary>
        /// <param name="hostFactory">
        ///     Builds a fresh host per generation (the same builder chain a <c>Program.cs</c> runs once
        ///     today). Each generation gets a fresh service provider, actor system, and service ids.
        /// </param>
        /// <param name="port">The port the web UI / API is served on.</param>
        /// <param name="cancellationToken">Cancelled to shut down (typically wired to Ctrl+C).</param>
        public static async Task RunAsync(Func<IDevHost> hostFactory, int port = 5000, CancellationToken cancellationToken = default)
        {
            var headless = Environment.GetEnvironmentVariable(NoBrowserEnvVar) == "1";
            var generation = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                generation++;
                await using var host = hostFactory();

                var resetRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var resetSubscription = host.Control.OnResetRequested(() => resetRequested.TrySetResult());

                await host.StartAsync(cancellationToken);

                if (TryExportConfig(host))
                {
                    await host.StopAsync(CancellationToken.None);
                    return;
                }

                if (headless)
                {
                    Console.WriteLine($"{{\"ready\":true,\"port\":{port},\"generation\":{generation}}}");
                }
                else if (generation == 1)
                {
                    // Open the browser once; on recycle the page reconnects by itself.
                    OpenBrowser($"http://localhost:{port}");
                }

                try
                {
                    await Task.WhenAny(resetRequested.Task, Task.Delay(Timeout.Infinite, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    // Expected on Ctrl+C / cancellation.
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    await host.StopAsync(CancellationToken.None);
                    return;
                }

                Console.WriteLine($"Reset requested — recycling host (generation {generation + 1})...");

                // `await using` disposes the old host at the end of this iteration; the brief delay lets
                // Kestrel finish releasing the port before the next generation rebinds it.
                await host.StopAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
            }
        }

        // One-shot export mode: write the wired configuration as JSON (camelCase, the /api/configuration
        // wire shape) and signal the caller to exit. Boot-dump-exit keeps `dale scenario validate`
        // CI-friendly — no port, no server lifetime to manage.
        private static bool TryExportConfig(IDevHost host)
        {
            var path = Environment.GetEnvironmentVariable(ExportConfigEnvVar);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var options = new JsonSerializerOptions
                          {
                              PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                              DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                              WriteIndented = true,
                          };
            File.WriteAllText(path, JsonSerializer.Serialize(host.Control.GetConfiguration(), options));
            Console.WriteLine($"{{\"exported\":\"{path.Replace("\\", "\\\\")}\"}}");
            return true;
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