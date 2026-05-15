using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Helpers
{
    /// <summary>
    ///     Shared validation and resolution logic used across commands.
    ///     Eliminates boilerplate for project discovery, target resolution, and error reporting.
    /// </summary>
    public static class CommandHelpers
    {
        /// <summary>
        ///     Find a Dale project or report an error. Returns null on failure (error already printed).
        ///     When in a solution directory, lists available Dale projects.
        /// </summary>
        public static DaleProject? RequireProject(string? projectPath)
        {
            var project = ProjectDiscovery.FindProject(projectPath);
            if (project != null)
            {
                return project;
            }

            // Give a better message if we're in a solution directory
            var solution = ProjectDiscovery.FindSolution();
            if (solution != null)
            {
                var daleProjects = FindDaleProjectsInSolution(solution);
                if (daleProjects.Count == 1)
                {
                    // Auto-select the only Dale project
                    var slnDir = Path.GetDirectoryName(solution) ?? ".";
                    var autoPath = Path.GetFullPath(Path.Combine(slnDir, daleProjects[0]));
                    return ProjectDiscovery.FindProject(autoPath);
                }

                if (daleProjects.Count > 1)
                {
                    DaleConsole.Error("Multiple Dale projects in solution. Use --project to specify:");
                    foreach (var p in daleProjects)
                    {
                        DaleConsole.Info($"  {p}");
                    }
                }
                else
                {
                    DaleConsole.Error("Solution found but contains no Dale projects.");
                }
            }
            else
            {
                DaleConsole.Error("No Dale project found. Run from a project directory or use --project.");
            }

            return null;
        }

        /// <summary>
        ///     Find a .sln or .csproj to pass to dotnet build/test. Returns null on failure (error already printed).
        /// </summary>
        public static string? RequireBuildTarget(string? projectPath)
        {
            var sln = ProjectDiscovery.FindSolution();
            var project = ProjectDiscovery.FindProject(projectPath);
            var target = sln ?? project?.CsprojPath;

            if (target == null)
            {
                DaleConsole.Error("No .sln or Dale project found. Run from a project directory or use --project.");
            }

            return target;
        }

        /// <summary>
        ///     Find logic blocks in the project and resolve the target.
        ///     Returns null on failure (error already printed).
        /// </summary>
        public static LogicBlockInfo? RequireTarget(DaleProject project, string? toOption)
        {
            var logicBlocks = ProjectDiscovery.FindLogicBlocks(project.ProjectDirectory);
            if (logicBlocks.Count == 0)
            {
                DaleConsole.Error("No LogicBlock classes found in the project.");
                return null;
            }

            var target = SourceInserter.ResolveTarget(logicBlocks, toOption);
            if (target != null)
            {
                return target;
            }

            if (toOption != null)
            {
                DaleConsole.Error($"LogicBlock '{toOption}' not found. Available:");
            }
            else
            {
                DaleConsole.Error("Multiple logic blocks found. Use --to <name> to specify which one:");
            }

            foreach (var lb in logicBlocks)
            {
                DaleConsole.Info($"  {lb.ClassName}");
            }

            return null;
        }

        /// <summary>
        ///     Parse a .sln file and find projects that reference Vion.Dale.Sdk.
        ///     Returns relative paths to .csproj files.
        /// </summary>
        private static List<string> FindDaleProjectsInSolution(string slnPath)
        {
            var results = new List<string>();
            var slnDir = Path.GetDirectoryName(slnPath) ?? ".";

            try
            {
                var slnContent = File.ReadAllText(slnPath);
                var projectPattern = new Regex(@"Project\("".+""\)\s*=\s*"".+""\s*,\s*""(.+?\.csproj)""", RegexOptions.Compiled);

                foreach (Match match in projectPattern.Matches(slnContent))
                {
                    var relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                    var absolutePath = Path.GetFullPath(Path.Combine(slnDir, relativePath));

                    if (!File.Exists(absolutePath))
                    {
                        continue;
                    }

                    // Check if this project has a PackageReference to Vion.Dale.Sdk (the core SDK, not TestKit etc.)
                    try
                    {
                        var csprojContent = File.ReadAllText(absolutePath);
                        if (Regex.IsMatch(csprojContent, @"PackageReference\s+Include\s*=\s*""Dale\.Sdk"""))
                        {
                            results.Add(relativePath);
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }
            catch
            {
                // Skip unparseable solution files
            }

            return results;
        }
    }
}