using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class DevCommand
    {
        public static Command Create()
        {
            // Unmatched tokens are collected (not parse errors) so scenario args reach the DevHost
            // app, e.g. `dale dev -- operator-steering` (see BuildRunArguments).
            var command = new Command("dev", "Start the DevHost with web UI (extra arguments are forwarded to the DevHost app)")
                          {
                              TreatUnmatchedTokensAsErrors = false,
                          };

            var headlessOption = new Option<bool>("--headless")
                                 {
                                     Description =
                                         "Run without opening a browser. Serves the control API and prints a JSON readiness line on stdout — for tools, CI, and agents.",
                                 };
            command.Options.Add(headlessOption);

            var exportConfigOption = new Option<string?>("--export-config")
                                     {
                                         Description =
                                             "Boot the wired network, write its configuration (block names, services, schemas, topology) as JSON to this file, and exit — the data source for `dale scenario validate` and `dale scenario schema` (RFC 0006).",
                                     };
            command.Options.Add(exportConfigOption);

            var exportTopologyOption = new Option<string?>("--export-topology")
                                       {
                                           Description =
                                               "Boot the wired network, write it as a *.topology.json dev profile to this file, and exit — the migration path from C# presets to topology files (RFC 0006 R5).",
                                       };
            command.Options.Add(exportTopologyOption);

            var presetOption = new Option<string?>("--preset")
                               {
                                   Description =
                                       "Pass <name> as the DevHost app's first program argument (args[0]) — the discoverable, composable form of `dale dev -- <name>` for selecting a consumer-defined preset. Combines with --export-config/--export-topology to export a non-default preset.",
                               };
            command.Options.Add(presetOption);

            var steppedOption = new Option<bool>("--stepped")
                                {
                                    Description =
                                        "Boot in deterministic stepping mode (a controllable virtual clock) so scenario runs — in the Player and via `dale scenario run` — step exactly instead of waiting on the wall clock. Timers idle between runs; use the default real-clock mode for live watching.",
                                };
            command.Options.Add(steppedOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");
                                  var headless = parseResult.GetValue(headlessOption);
                                  var exportConfig = parseResult.GetValue(exportConfigOption);
                                  var exportTopology = parseResult.GetValue(exportTopologyOption);
                                  var preset = parseResult.GetValue(presetOption);
                                  var stepped = parseResult.GetValue(steppedOption);

                                  // The DevHost process (consumer-owned Program.cs via DevHostWebRunner) reads this
                                  // env var to skip the browser and emit a readiness line. UseShellExecute=false in
                                  // DotnetRunner means the spawned `dotnet run` inherits it. Name hardcoded to keep
                                  // the CLI's no-SDK-dependency rule (mirrors DevHostWebRunner.NoBrowserEnvVar).
                                  if (headless || exportConfig != null || exportTopology != null)
                                  {
                                      Environment.SetEnvironmentVariable("DALE_DEVHOST_NO_BROWSER", "1");
                                  }

                                  // Deterministic stepping: the consumer's WithWebUi() reads this and boots a
                                  // controllable clock. Hardcoded name to keep the CLI's no-SDK-dependency rule
                                  // (mirrors DevHostWebRunner.SteppedEnvVar).
                                  if (stepped)
                                  {
                                      Environment.SetEnvironmentVariable("DALE_DEVHOST_STEPPED", "1");
                                  }

                                  // One-shot exports (boot, dump, exit) — mirror DevHostWebRunner.Export*EnvVar.
                                  if (exportConfig != null)
                                  {
                                      Environment.SetEnvironmentVariable("DALE_DEVHOST_EXPORT_CONFIG", Path.GetFullPath(exportConfig));
                                  }

                                  if (exportTopology != null)
                                  {
                                      Environment.SetEnvironmentVariable("DALE_DEVHOST_EXPORT_TOPOLOGY", Path.GetFullPath(exportTopology));
                                  }

                                  // Strategy: find the DevHost .csproj by searching for {Name}.DevHost.csproj
                                  var devHostCsproj = FindDevHostProject(projectPath);
                                  if (devHostCsproj == null)
                                  {
                                      DaleConsole.Error("DevHost project not found. Ensure a {Name}.DevHost project exists in the solution.");
                                      return 1;
                                  }

                                  var devHostName = Path.GetFileNameWithoutExtension(devHostCsproj);
                                  var workingDir = Path.GetDirectoryName(Path.GetDirectoryName(devHostCsproj)) ?? ".";

                                  DaleConsole.Info($"Starting {devHostName}{(headless ? " (headless)" : "")}...");
                                  DaleConsole.Info(headless ? "  Control API at http://localhost:5000/api (no browser)" : "  Web UI at http://localhost:5000");
                                  DaleConsole.Blank();

                                  var runArguments = BuildRunArguments(devHostCsproj, parseResult.UnmatchedTokens, preset);

                                  if (exportConfig != null || exportTopology != null)
                                  {
                                      // The env vars above already carry the absolute export paths.
                                      var exportFiles = new List<string>();
                                      if (exportConfig != null)
                                      {
                                          exportFiles.Add(Path.GetFullPath(exportConfig));
                                      }

                                      if (exportTopology != null)
                                      {
                                          exportFiles.Add(Path.GetFullPath(exportTopology));
                                      }

                                      return await RunWithBootWindowAsync(token => DotnetRunner.RunAsync("run", runArguments, workingDir, token),
                                                                          () => exportFiles.All(File.Exists),
                                                                          TimeSpan.FromSeconds(120),
                                                                          TimeSpan.FromSeconds(15));
                                  }

                                  return await DotnetRunner.RunAsync("run", runArguments, workingDir);
                              });

            return command;
        }

        // Export modes are boot-dump-exit: DevHostWebRunner writes the file(s) and exits. A Program.cs that
        // predates DevHostWebRunner ignores the DALE_DEVHOST_EXPORT_* env vars and runs forever — so bound
        // the wait instead of hanging the CLI silently. Once an export file appears the host clearly honored
        // the request (a slow cold build is fine — we only stop watching the clock then); a genuine
        // non-cooperating hang (no file within the window) is killed with an actionable hint (DF-01). The
        // runner and clock windows are injected so the three outcomes are unit-testable without a real
        // process.
        internal static async Task<int> RunWithBootWindowAsync(Func<CancellationToken, Task<int>> run,
                                                               Func<bool> exportFilesWritten,
                                                               TimeSpan bootWindow,
                                                               TimeSpan graceAfterExport)
        {
            using var cts = new CancellationTokenSource();
            var runTask = run(cts.Token);
            var stopwatch = Stopwatch.StartNew();
            TimeSpan? graceDeadline = null;

            while (true)
            {
                if (await Task.WhenAny(runTask, Task.Delay(50)) == runTask)
                {
                    var exitCode = await runTask;

                    // The host exited on its own. Normally that's the export-and-exit path — but a freshly
                    // restored `dotnet run` can occasionally boot in serve mode and exit without honoring the
                    // export (DF-16, a one-time rebuild race). If it exited cleanly yet wrote nothing, surface
                    // it instead of reporting a silent success.
                    if (exitCode == 0 && !exportFilesWritten())
                    {
                        DaleConsole.Error("DevHost exited without writing the export. This can happen on the first run right after a package bump " +
                                          "(a `dotnet run` rebuild race) — re-run the command.");
                        return 1;
                    }

                    return exitCode;
                }

                if (graceDeadline is null && exportFilesWritten())
                {
                    // The export happened; the host should exit imminently. Stop racing the boot window.
                    graceDeadline = stopwatch.Elapsed + graceAfterExport;
                }

                if (graceDeadline is { } grace && stopwatch.Elapsed > grace)
                {
                    // File(s) written but the process lingered — the export succeeded; stop the stray host.
                    await CancelAndAwait(cts, runTask);
                    return 0;
                }

                if (graceDeadline is null && stopwatch.Elapsed > bootWindow)
                {
                    await CancelAndAwait(cts, runTask);
                    DaleConsole.Error($"DevHost did not honor the export within {bootWindow.TotalSeconds:F0}s and was stopped. " +
                                      "Ensure the DevHost's Program.cs runs via DevHostWebRunner.RunAsync — the --export-config / --export-topology modes live there.");
                    return 1;
                }
            }
        }

        /// <summary>
        ///     Builds the <c>dotnet run</c> arguments for the DevHost: the target project, then the program
        ///     arguments after <c>--</c> — the optional <paramref name="preset" /> value first (so it lands as
        ///     <c>args[0]</c>, the consumer's preset/scenario switch), followed by any extra tokens the user
        ///     passed after <c>dale dev --</c> (e.g. <c>dale dev -- operator-steering</c>). Program arguments are
        ///     delimited with <c>--</c> so <c>dotnet run</c> passes them to the application verbatim — including
        ///     option-like names (a leading <c>-</c>, or names that collide with <c>dotnet run</c> flags) — rather
        ///     than trying to interpret them itself.
        /// </summary>
        internal static List<string> BuildRunArguments(string devHostCsproj, IReadOnlyList<string> forwardedArgs, string? preset = null)
        {
            var args = new List<string> { "--project", devHostCsproj };

            var programArgs = new List<string>();
            if (!string.IsNullOrEmpty(preset))
            {
                programArgs.Add(preset);
            }

            programArgs.AddRange(forwardedArgs);

            if (programArgs.Count > 0)
            {
                args.Add("--");
                args.AddRange(programArgs);
            }

            return args;
        }

        private static async Task CancelAndAwait(CancellationTokenSource cts, Task<int> run)
        {
            cts.Cancel();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
                // Expected — we cancelled the run to stop a hung or lingering host.
            }
        }

        /// <summary>
        ///     Find a runnable *.DevHost.csproj near the current location. Search order:
        ///     1. Current directory — user is inside the DevHost project itself.
        ///     2. Subdirectories — user is at the solution root, DevHost is nested below.
        ///     3. Sibling directories — user is in the library project, DevHost is next to it.
        ///     Subdirectories precede siblings so that a freshly-created project sitting under
        ///     a parent that contains unrelated *.DevHost projects (e.g. the SDK repo's own
        ///     Vion.Dale.DevHost library) doesn't get matched to the wrong one. Matches are
        ///     further filtered to runnable projects (OutputType Exe or Program.cs present),
        ///     so DevHost *libraries* are skipped.
        /// </summary>
        private static string? FindDevHostProject(string? projectPath)
        {
            var startDir = projectPath != null ? Path.GetDirectoryName(Path.GetFullPath(projectPath)) : Directory.GetCurrentDirectory();
            if (startDir == null)
            {
                return null;
            }

            // 1. CWD itself
            var match = FindRunnableDevHost(startDir);
            if (match != null)
            {
                return match;
            }

            // 2. Subdirectories of CWD
            foreach (var dir in Directory.GetDirectories(startDir))
            {
                match = FindRunnableDevHost(dir);
                if (match != null)
                {
                    return match;
                }
            }

            // 3. Siblings (parent's children, which includes startDir but re-check is harmless)
            var parentDir = Directory.GetParent(startDir)?.FullName ?? startDir;
            foreach (var dir in Directory.GetDirectories(parentDir))
            {
                match = FindRunnableDevHost(dir);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string? FindRunnableDevHost(string dir)
        {
            foreach (var csproj in Directory.GetFiles(dir, "*.DevHost.csproj"))
            {
                if (IsRunnableProject(csproj))
                {
                    return csproj;
                }
            }

            return null;
        }

        private static bool IsRunnableProject(string csprojPath)
        {
            try
            {
                var content = File.ReadAllText(csprojPath);
                if (content.Contains("<OutputType>Exe</OutputType>"))
                {
                    return true;
                }

                // A DevHost library has no Program.cs; a runnable DevHost app does.
                var projectDir = Path.GetDirectoryName(csprojPath);
                return projectDir != null && File.Exists(Path.Combine(projectDir, "Program.cs"));
            }
            catch
            {
                return false;
            }
        }
    }
}