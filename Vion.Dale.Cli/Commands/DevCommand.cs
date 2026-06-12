using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
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

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");
                                  var headless = parseResult.GetValue(headlessOption);

                                  // The DevHost process (consumer-owned Program.cs via DevHostWebRunner) reads this
                                  // env var to skip the browser and emit a readiness line. UseShellExecute=false in
                                  // DotnetRunner means the spawned `dotnet run` inherits it. Name hardcoded to keep
                                  // the CLI's no-SDK-dependency rule (mirrors DevHostWebRunner.NoBrowserEnvVar).
                                  if (headless)
                                  {
                                      Environment.SetEnvironmentVariable("DALE_DEVHOST_NO_BROWSER", "1");
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

                                  var runArguments = BuildRunArguments(devHostCsproj, parseResult.UnmatchedTokens);
                                  return await DotnetRunner.RunAsync("run", runArguments, workingDir);
                              });

            return command;
        }

        /// <summary>
        ///     Builds the <c>dotnet run</c> arguments for the DevHost: the target project, then any extra tokens
        ///     the user passed after <c>--</c> (e.g. <c>dale dev -- operator-steering</c> to select a
        ///     consumer-defined scenario the DevHost's <c>Program.cs</c> switches on). Forwarded tokens are
        ///     delimited with <c>--</c> so <c>dotnet run</c> passes them to the application verbatim — including
        ///     option-like names (a leading <c>-</c>, or names that collide with <c>dotnet run</c> flags) — rather
        ///     than trying to interpret them itself.
        /// </summary>
        internal static List<string> BuildRunArguments(string devHostCsproj, IReadOnlyList<string> forwardedArgs)
        {
            var args = new List<string> { "--project", devHostCsproj };

            if (forwardedArgs.Count > 0)
            {
                args.Add("--");
                args.AddRange(forwardedArgs);
            }

            return args;
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