using System.CommandLine;
using System.IO;
using System.Linq;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class DevCommand
    {
        public static Command Create()
        {
            var command = new Command("dev", "Start the DevHost with web UI");

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  // Strategy: find the DevHost .csproj by searching for {Name}.DevHost.csproj
                                  var devHostCsproj = FindDevHostProject(projectPath);
                                  if (devHostCsproj == null)
                                  {
                                      DaleConsole.Error("DevHost project not found. Ensure a {Name}.DevHost project exists in the solution.");
                                      return 1;
                                  }

                                  var devHostName = Path.GetFileNameWithoutExtension(devHostCsproj);
                                  var workingDir = Path.GetDirectoryName(Path.GetDirectoryName(devHostCsproj)) ?? ".";

                                  DaleConsole.Info($"Starting {devHostName}...");
                                  DaleConsole.Info("  Web UI at http://localhost:5000");
                                  DaleConsole.Blank();

                                  var args = new[] { "--project", devHostCsproj }.Concat(parseResult.UnmatchedTokens).ToList();
                                  return await DotnetRunner.RunAsync("run", args, workingDir);
                              });

            return command;
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
