using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;

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
        ///     One-shot export mode (RFC 0006 R5): boot, write the wired network as a
        ///     <c>*.topology.json</c> dev profile (instances, interface mappings, contract mappings), exit —
        ///     the migration path from C# presets to topology files.
        /// </summary>
        public const string ExportTopologyEnvVar = "DALE_DEVHOST_EXPORT_TOPOLOGY";

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

            if (TryExport(host))
            {
                await host.StopAsync(CancellationToken.None);
                return;
            }

            if (headless)
            {
                // Single-line, parseable readiness signal — lets an agent that spawned this process know
                // the network is up and on which port before it starts driving /api.
                WriteJsonLine(new { ready = true, port });
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
        public static Task RunAsync(Func<IDevHost> hostFactory, int port = 5000, CancellationToken cancellationToken = default)
        {
            return RunAsync(_ => hostFactory(), port, cancellationToken);
        }

        /// <summary>
        ///     Topology-aware supervised variant (RFC 0006 R5): the factory receives the topology id the UI
        ///     requested via <c>POST /api/topologies/{id}/switch</c> (null = the default preset, and on a
        ///     plain reset the previous selection is kept). A typical consumer composes
        ///     <c>DevTopologyLoader.Load(topologyId)</c> for non-null ids and its C# preset otherwise.
        /// </summary>
        public static async Task RunAsync(Func<string?, IDevHost> hostFactory, int port = 5000, CancellationToken cancellationToken = default)
        {
            var headless = Environment.GetEnvironmentVariable(NoBrowserEnvVar) == "1";
            var generation = 0;
            string? topologyId = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                generation++;
                await using var host = hostFactory(topologyId);

                var resetRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var resetSubscription = host.Control.OnResetRequested(() => resetRequested.TrySetResult());

                await host.StartAsync(cancellationToken);

                if (TryExport(host))
                {
                    // `await using var host` (above) disposes — and stops — the host exactly once when this
                    // returns. An explicit StopAsync here would make DisposeAsync stop an already-stopped,
                    // disposed host, throwing ObjectDisposedException from WebHostService.StopAsync (DF-08).
                    return;
                }

                if (headless)
                {
                    WriteJsonLine(new { ready = true, port, generation });
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

                // A topology switch rides the reset signal; a plain reset keeps the current selection.
                topologyId = host.Control.RequestedTopology ?? topologyId;

                Console.WriteLine($"Reset requested — recycling host (generation {generation + 1})...");

                // `await using` disposes the old host at the end of this iteration; the brief delay lets
                // Kestrel finish releasing the port before the next generation rebinds it.
                await host.StopAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
            }
        }

        /// <summary>
        ///     Folder-driven supervised variant: discovers topologies from the
        ///     <c>topologies/</c> directory (resolved via <see cref="DevDataDirectory" />), generates and
        ///     writes <c>topologies/default.topology.json</c> when none exists, and runs the supervised
        ///     recycle loop exactly like <see cref="RunAsync(Func{string?,IDevHost},int,CancellationToken)" />.
        ///     <para>
        ///         Consumers supply only DI registration and optional extras (<c>ConfigureLogging</c>) via
        ///         <paramref name="configure" /> — no <c>WithConfiguration</c>. The runner owns topology
        ///         discovery, generation, and loading.
        ///     </para>
        /// </summary>
        /// <param name="configure">
        ///     Applies <c>WithDi&lt;TDi&gt;</c>, <c>WithWebUi(port)</c>, and optionally
        ///     <c>ConfigureLogging</c> to the builder. Must NOT call <c>WithConfiguration</c>.
        /// </param>
        /// <param name="port">The port the web UI / API is served on.</param>
        /// <param name="cancellationToken">Cancelled to shut down (typically wired to Ctrl+C).</param>
        public static Task RunFolderDrivenAsync(Action<DevHostBuilder> configure, int port = 5000, CancellationToken cancellationToken = default)
        {
            // Resolve the topologies directory once (same resolution logic DevTopologyStore/DevTopologyLoader
            // use — so the boot resolution and the switching store always agree on the directory).
            var topologiesDir = DevDataDirectory.Resolve("topologies", null);

            // Boot: discover committed topologies; auto-generate default if none found. The catalog
            // enumeration from a temporary builder avoids mutating a real builder that also calls Build().
            var catalogBuilder = DevHostBuilder.Create();
            configure(catalogBuilder);
            var catalog = catalogBuilder.GetBlockCatalog();
            var bootId = ResolveBootTopologyId(catalog, topologiesDir);

            IDevHost Factory(string? requestedId)
            {
                var builder = DevHostBuilder.Create();
                configure(builder);

                // For non-null requested ids (topology-switch) the UI supplies an id the store knows about.
                // For null (plain reset) we keep the last resolved boot id (topologyId in RunAsync stays
                // the previous selection), so fall back to the last resolved boot id.
                var id = requestedId ?? bootId;
                builder.WithConfiguration(DevTopologyLoader.Load(id, topologiesDir));
                return builder.Build();
            }

            return RunAsync(Factory, port, cancellationToken);
        }

        /// <summary>
        ///     Resolve which topology id to boot with, given the discovered catalog and topology directory.
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 If committed topologies exist: return <c>"default"</c> when that id is present, or
        ///                 the first id alphabetically otherwise.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 If none exist: generate <c>default.topology.json</c>, announce it on the console,
        ///                 and return <c>"default"</c>.
        ///             </description>
        ///         </item>
        ///     </list>
        /// </summary>
        public static string ResolveBootTopologyId(IReadOnlyCollection<Type> catalog, string? topologiesDir)
        {
            var store = new DevTopologyStore(topologiesDir);
            var list = store.List();

            if (list.Count > 0)
            {
                return list.Any(e => string.Equals(e.Id, "default", StringComparison.OrdinalIgnoreCase)) ? "default" :
                           list.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).First().Id;
            }

            // No committed topology — generate one and announce it.
            var path = DefaultTopologyGenerator.WriteDefault(catalog, topologiesDir);
            Console.WriteLine($"No topology found — generated {path} (each block once, auto-connected). Edit it, commit it, or add it to .gitignore.");
            return "default";
        }

        // One-shot export modes: write the wired configuration (the /api/configuration wire shape) and/or
        // the topology dev profile, then signal the caller to exit. Boot-dump-exit keeps
        // `dale scenario validate` and `dale dev --export-topology` CI-friendly — no port, no server
        // lifetime to manage.
        private static bool TryExport(IDevHost host)
        {
            var exported = false;

            var configPath = Environment.GetEnvironmentVariable(ExportConfigEnvVar);
            if (!string.IsNullOrEmpty(configPath))
            {
                var options = new JsonSerializerOptions
                              {
                                  PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                  DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                                  WriteIndented = true,
                              };
                File.WriteAllText(configPath, JsonSerializer.Serialize(host.Control.GetConfiguration(), options));
                WriteJsonLine(new { exported = configPath });
                exported = true;
            }

            var topologyPath = Environment.GetEnvironmentVariable(ExportTopologyEnvVar);
            if (!string.IsNullOrEmpty(topologyPath))
            {
                File.WriteAllText(topologyPath, DevTopologyFile.FromConfiguration(host.Control.GetConfiguration()).ToJson());
                WriteJsonLine(new { exported = topologyPath });
                exported = true;
            }

            return exported;
        }

        // Machine-readable single-line JSON receipts for tools/agents parsing stdout (readiness signals,
        // export receipts). JsonSerializer escapes paths correctly — notably backslashes on Windows — so the
        // receipt stays valid JSON without hand-rolled string building (DF-13).
        private static void WriteJsonLine(object value)
        {
            Console.WriteLine(JsonSerializer.Serialize(value));
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