using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Helpers
{
    /// <summary>
    ///     Shared validation and resolution logic used across commands.
    ///     Eliminates boilerplate for project discovery, target resolution, and error reporting.
    /// </summary>
    public static class CommandHelpers
    {
        private static string? _toolVersionOverride;

        private static string ToolVersion
        {
            get => _toolVersionOverride ?? Program.Version();
        }

        /// <summary>
        ///     Find a Dale project or report an error. Returns null on failure (error already printed).
        ///     When in a solution directory, lists available Dale projects.
        /// </summary>
        public static DaleProject? RequireProject(string? projectPath)
        {
            if (projectPath != null)
            {
                // An explicit --project is an instruction, not a hint: resolving some other project
                // instead would edit or publish something the caller did not name.
                var named = ProjectDiscovery.FindProject(projectPath);
                if (named == null)
                {
                    DaleConsole.Error(DescribeUnusableProjectPath(projectPath));
                }
                else
                {
                    WarnIfToolOlderThanSdk(named);
                }

                return named;
            }

            var project = ProjectDiscovery.FindProject();
            if (project != null)
            {
                WarnIfToolOlderThanSdk(project);
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
                    var autoSelected = ProjectDiscovery.FindProject(autoPath);
                    if (autoSelected != null)
                    {
                        WarnIfToolOlderThanSdk(autoSelected);
                    }

                    return autoSelected;
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
        ///     Find a solution (.sln/.slnx) or .csproj to pass to dotnet build/test. Returns null on failure (error already
        ///     printed).
        /// </summary>
        public static string? RequireBuildTarget(string? projectPath)
        {
            if (projectPath != null)
            {
                // The flag names one project; a solution above the working directory does not override it.
                return RequireProject(projectPath)?.CsprojPath;
            }

            var solution = ProjectDiscovery.FindSolution();
            if (solution != null)
            {
                return solution;
            }

            var project = ProjectDiscovery.FindProject();
            if (project == null)
            {
                DaleConsole.Error("No solution (.sln/.slnx) or Dale project found. Run from a project directory or use --project.");
            }

            return project?.CsprojPath;
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
        ///     The version this tool reports itself as, or null for the running assembly's own. The one place
        ///     it is chosen: <c>CliComposition</c> passes the real assembly version at start-up and a test
        ///     passes a released-looking one, which is what lets the stale-tool caution be proven — a local
        ///     build reports <c>0.0.0-local</c>, and <see cref="DescribeStaleTool" /> is silent for that by
        ///     design, so without the seam the wiring could be removed and no test would notice.
        /// </summary>
        internal static void UseToolVersion(string? version)
        {
            _toolVersionOverride = version;
        }

        /// <summary>
        ///     Why an explicitly named <c>--project</c> could not be used. The two causes need different
        ///     actions from the reader, and neither is "use --project", which they just did.
        /// </summary>
        internal static string DescribeUnusableProjectPath(string projectPath)
        {
            var fullPath = Path.GetFullPath(projectPath);
            return File.Exists(fullPath) ? $"'{fullPath}' is not a Dale project — it references neither the Vion.Dale.Sdk package nor the SDK project." :
                       $"No project file at '{fullPath}'.";
        }

        /// <summary>
        ///     Parse a solution file (.sln or .slnx) and find projects that reference Vion.Dale.Sdk.
        ///     Returns relative paths to .csproj files.
        /// </summary>
        internal static List<string> FindDaleProjectsInSolution(string slnPath)
        {
            var results = new List<string>();
            var slnDir = Path.GetDirectoryName(slnPath) ?? ".";

            try
            {
                foreach (var csprojPath in ExtractCsprojPaths(slnPath))
                {
                    var relativePath = csprojPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    var absolutePath = Path.GetFullPath(Path.Combine(slnDir, relativePath));

                    // Reuse the single source of truth: a project is a Dale project iff it references
                    // Vion.Dale.Sdk (PackageReference or ProjectReference). ParseCsproj handles a missing
                    // file, attribute order, casing, and parse errors by returning null — so TestKit /
                    // DevHost projects (which reference Vion.Dale.Sdk.* but not the core SDK) are excluded.
                    if (ProjectDiscovery.FindProject(absolutePath) != null)
                    {
                        results.Add(relativePath);
                    }
                }
            }
            catch
            {
                // Skip unparseable solution files
            }

            return results;
        }

        /// <summary>
        ///     Warn when this tool is older than the SDK the project references. A stale global tool renders
        ///     the previous release's view of a project that has already moved on, and nothing in the output
        ///     says which half is old — a post-release check reads it as a failed release rather than a tool
        ///     that needs updating. Only the older direction is a trap: a newer tool against a pinned older
        ///     SDK is a deliberate pin.
        /// </summary>
        internal static void WarnIfToolOlderThanSdk(DaleProject project)
        {
            // DaleConsole.Warning is what keeps this out of the JSON document (AC-CLI-001.5).
            var caution = DescribeStaleTool(project.SdkVersion, ToolVersion);
            if (caution != null)
            {
                DaleConsole.Warning(caution);
            }
        }

        /// <summary>
        ///     The caution for a tool older than the SDK a project references, or null where there is nothing
        ///     to say. Separated from the printing so every branch is decidable without a console.
        /// </summary>
        internal static string? DescribeStaleTool(string? referencedSdk, string toolVersion)
        {
            var referenced = ReleaseCore(referencedSdk);
            var tool = ReleaseCore(toolVersion);
            if (referenced == null || tool == null || tool >= referenced)
            {
                return null;
            }

            return $"This tool is {toolVersion}; the project references Vion.Dale.Sdk {referencedSdk}. " + $"Run: dotnet tool update -g Vion.Dale.Cli --version {referencedSdk}";
        }

        /// <summary>
        ///     The <c>major.minor.patch</c> of a released version, or null when there is nothing to compare:
        ///     an unparseable string, or a <c>0.0.0</c> build. A local or untagged CI build is on no feed and
        ///     carries no ordering against a released SDK, so warning about it would fire on every run inside
        ///     this repository. Pre-release suffixes are dropped rather than ordered — the trap is a whole
        ///     release behind, and a stricter comparison would warn on a preview pinned on purpose.
        /// </summary>
        private static Version? ReleaseCore(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var core = version.Split('-', '+')[0];
            if (!Version.TryParse(core, out var parsed))
            {
                return null;
            }

            var normalized = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return normalized == new Version(0, 0, 0) ? null : normalized;
        }

        /// <summary>
        ///     Extract .csproj paths (as written, relative to the solution directory) from a
        ///     classic .sln or an XML .slnx solution file.
        /// </summary>
        private static IEnumerable<string> ExtractCsprojPaths(string slnPath)
        {
            if (string.Equals(Path.GetExtension(slnPath), ".slnx", StringComparison.OrdinalIgnoreCase))
            {
                // XML solution format: <Project Path="MyLib/MyLib.csproj" /> entries, optionally
                // nested inside <Folder> elements.
                var doc = XDocument.Load(slnPath);
                return doc.Descendants("Project")
                          .Select(p => p.Attribute("Path")?.Value)
                          .Where(p => p != null && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                          .Select(p => p!)
                          .ToList();
            }

            var slnContent = File.ReadAllText(slnPath);
            var projectPattern = new Regex(@"Project\("".+""\)\s*=\s*"".+""\s*,\s*""(.+?\.csproj)""", RegexOptions.Compiled);
            return projectPattern.Matches(slnContent).Select(m => m.Groups[1].Value).ToList();
        }
    }
}