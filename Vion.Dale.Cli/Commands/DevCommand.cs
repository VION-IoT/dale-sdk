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
        ///     Find a *.DevHost.csproj in the solution. Searches:
        ///     1. Current directory (if it IS the DevHost project)
        ///     2. Sibling directories (from library project or solution level)
        ///     3. Via RequireProject to find the library, then look for its DevHost sibling
        /// </summary>
        private static string? FindDevHostProject(string? projectPath)
        {
            var startDir = projectPath != null ? Path.GetDirectoryName(Path.GetFullPath(projectPath)) : Directory.GetCurrentDirectory();
            if (startDir == null)
            {
                return null;
            }

            // Check if current directory IS a DevHost project
            var currentDirCsprojs = Directory.GetFiles(startDir, "*.DevHost.csproj");
            if (currentDirCsprojs.Length > 0)
            {
                return currentDirCsprojs[0];
            }

            // Check sibling directories (works from solution level)
            var parentDir = Directory.GetParent(startDir)?.FullName ?? startDir;
            foreach (var dir in Directory.GetDirectories(parentDir))
            {
                var devHostFiles = Directory.GetFiles(dir, "*.DevHost.csproj");
                if (devHostFiles.Length > 0)
                {
                    return devHostFiles[0];
                }
            }

            // Check subdirectories (works from solution level when DevHost is nested)
            foreach (var dir in Directory.GetDirectories(startDir))
            {
                var devHostFiles = Directory.GetFiles(dir, "*.DevHost.csproj");
                if (devHostFiles.Length > 0)
                {
                    return devHostFiles[0];
                }
            }

            return null;
        }
    }
}
