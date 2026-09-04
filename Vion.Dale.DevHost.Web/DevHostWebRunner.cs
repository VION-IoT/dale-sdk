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
    ///     tools/agents. Centralizes what each generated DevHost <c>Program.cs</c> used to do
    ///     by hand, so headless mode is consistent and the browser/readiness logic lives in one place.
    /// </summary>
    public static class DevHostWebRunner
    {
        /// <summary>The env var that switches a web DevHost into headless (no-browser) mode.</summary>
        public const string NoBrowserEnvVar = "DALE_DEVHOST_NO_BROWSER";

        /// <summary>
        ///     The env var (<c>=1</c>) that boots the web DevHost in deterministic stepping mode — a
        ///     controllable clock so server-side scenario runs (Player + <c>dale scenario run</c>) step
        ///     exactly instead of waiting on the wall clock. Set by <c>dale dev --stepped</c>; read by
        ///     <see cref="DevHostBuilderExtensions.WithWebUi" />.
        /// </summary>
        public const string SteppedEnvVar = "DALE_DEVHOST_STEPPED";

        /// <summary>
        ///     One-shot export mode: when set to a file path, the runner boots the host, writes
        ///     the wired network's <c>ConfigurationOutput</c> JSON to that path (the same shape
        ///     <c>GET /api/configuration</c> serves — block instance names, service identifiers, schemas,
        ///     topology name), and exits. <c>dale scenario validate</c> / <c>schema</c> consume the export.
        /// </summary>
        public const string ExportConfigEnvVar = "DALE_DEVHOST_EXPORT_CONFIG";

        /// <summary>
        ///     One-shot export mode: boot, write the wired network as a
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
                // The same shape the supervised loop prints, generation and all: a parser written against one
                // overload is written against the other, and this host is generation 1 by construction.
                WriteJsonLine(new { ready = true, port, generation = 1 });
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
        ///     Topology-aware supervised variant: the factory receives the topology id the UI
        ///     requested via <c>POST /api/topologies/{id}/switch</c> (null = the default preset, and on a
        ///     plain reset the previous selection is kept). A typical consumer composes
        ///     <c>DevTopologyLoader.Load(topologyId)</c> for non-null ids and its C# preset otherwise.
        /// </summary>
        public static async Task RunAsync(Func<string?, IDevHost> hostFactory, int port = 5000, CancellationToken cancellationToken = default)
        {
            var headless = Environment.GetEnvironmentVariable(NoBrowserEnvVar) == "1";
            var generation = 0;
            string? topologyId = null;

            // The last topology that actually started. A switch onto a topology that cannot resolve falls
            // back to it rather than taking the process down (see the catch below).
            string? runningTopologyId = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                generation++;

                // BUILDING the host is inside the guard, not only starting it: a topology file deleted between
                // the editor's listing and the switch throws FileNotFoundException from the factory, and one
                // that no longer builds throws InvalidDataException — both from `hostFactory`, which used to sit
                // outside the try and take the process down where an unregistered block only printed a line.
                IDevHost host;
                try
                {
                    host = hostFactory(topologyId);
                }
                catch (Exception exception) when (IsRecoverableTopologyFailure(exception, topologyId, runningTopologyId, cancellationToken))
                {
                    WriteTopologyFallback(topologyId, runningTopologyId, exception);
                    topologyId = runningTopologyId;
                    continue;
                }
                catch (Exception exception)
                {
                    // A BOOT generation, or a fallback that itself failed: nothing can be recycled onto, so the
                    // process ends — but not silently. The readiness line is what an agent waits for; this is
                    // its counterpart, so a spawning caller learns of the failure instead of waiting out its
                    // own timeout.
                    WriteJsonLine(new { failed = true, port, generation, topology = topologyId, reason = exception.Message });
                    throw;
                }

                await using (host)
                {
                    var resetRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var resetSubscription = host.Control.OnResetRequested(() => resetRequested.TrySetResult());

                    try
                    {
                        await host.StartAsync(cancellationToken);
                    }
                    catch (Exception exception) when (IsRecoverableTopologyFailure(exception, topologyId, runningTopologyId, cancellationToken))
                    {
                        // A topology the UI switched to can refuse to resolve — e.g. it names a block whose
                        // services.AddTransient<T>() line is missing, which DevHostIntrospection rejects
                        // (VION-66). Killing `dale dev` here would take away the very UI the operator needs to
                        // pick another topology, so the supervisor reports the failure and recycles back onto
                        // the topology that was running. Introspection throws before any hosted service starts,
                        // so nothing was bound and the disposal at the end of this block is enough.
                        WriteTopologyFallback(topologyId, runningTopologyId, exception);
                        topologyId = runningTopologyId;
                        continue;
                    }
                    catch (Exception exception)
                    {
                        WriteJsonLine(new { failed = true, port, generation, topology = topologyId, reason = exception.Message });
                        throw;
                    }

                    runningTopologyId = topologyId;

                    if (TryExport(host))
                    {
                        // `await using` (above) disposes — and stops — the host exactly once when this returns.
                        // An explicit StopAsync here would make DisposeAsync stop an already-stopped, disposed
                        // host, throwing ObjectDisposedException from WebHostService.StopAsync (DF-08).
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

                    // Clock-mode switch: a requested mode rides the reset. Set the env var the next
                    // generation's WithWebUi reads, so the rebuilt host boots stepped or real as asked.
                    // Persists across later recycles until toggled again. (No-op for custom factories that
                    // don't read it.)
                    if (host.Control.RequestedClockMode is { } requestedStepped)
                    {
                        Environment.SetEnvironmentVariable(SteppedEnvVar, requestedStepped ? "1" : "0");
                    }

                    Console.WriteLine($"Reset requested — recycling host (generation {generation + 1})...");

                    // The disposal at the end of this block releases the old host; the brief delay lets Kestrel
                    // finish releasing the port before the next generation rebinds it.
                    await host.StopAsync(CancellationToken.None);
                    await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
                }
            }
        }

        // A generation that failed on a topology the UI switched TO can fall back; a boot generation and a
        // fallback that itself failed cannot, and end the process. The three exception types are the three ways
        // a topology refuses: an unregistered block (introspection), a file that is gone, and one that no
        // longer builds.
        private static bool IsRecoverableTopologyFailure(Exception exception, string? topologyId, string? runningTopologyId, CancellationToken cancellationToken)
        {
            return exception is InvalidOperationException or InvalidDataException or FileNotFoundException && topologyId != runningTopologyId &&
                   !cancellationToken.IsCancellationRequested;
        }

        private static void WriteTopologyFallback(string? topologyId, string? runningTopologyId, Exception exception)
        {
            Console.WriteLine($"Topology '{topologyId}' cannot start — staying on " + (runningTopologyId is null ? "the default topology" : $"'{runningTopologyId}'") +
                              $".{Environment.NewLine}{exception.Message}");
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
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                RequireWritableTarget(configPath, ExportConfigEnvVar);

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
            if (!string.IsNullOrWhiteSpace(topologyPath))
            {
                RequireWritableTarget(topologyPath, ExportTopologyEnvVar);

                File.WriteAllText(topologyPath, DevTopologyFile.FromConfiguration(host.Control.GetConfiguration()).ToJson());
                WriteJsonLine(new { exported = topologyPath });
                exported = true;
            }

            return exported;
        }

        // An export path must name a file this process can write. A whitespace-only value (a shell that quoted
        // an empty variable) used to pass the emptiness check, and a missing parent directory was not checked
        // at all — both surfaced as a framework exception out of File.WriteAllText, after the whole network had
        // already booted. The CLI guards its own side; a caller setting the variable directly had nothing.
        private static void RequireWritableTarget(string path, string variable)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException($"{variable} is not a usable file path ('{path}'): {exception.Message}", exception);
            }

            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                throw new InvalidOperationException($"{variable} points into a folder that does not exist ('{directory}'). Create it and re-run.");
            }
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