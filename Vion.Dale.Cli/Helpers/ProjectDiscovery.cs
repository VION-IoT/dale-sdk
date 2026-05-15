using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Vion.Dale.Cli.Helpers
{
    public class DaleProject
    {
        public string CsprojPath { get; init; } = string.Empty;

        public string ProjectName { get; init; } = string.Empty;

        public string ProjectDirectory { get; init; } = string.Empty;

        public string? PackageId { get; init; }

        public string? Version { get; init; }

        public string? SdkVersion { get; init; }

        public string? RootNamespace { get; init; }
    }

    public class LogicBlockInfo
    {
        public string ClassName { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;
    }

    public static class ProjectDiscovery
    {
        /// <summary>
        ///     Find the nearest .csproj referencing Vion.Dale.Sdk, walking up from startDirectory.
        ///     If projectPath is specified, uses that directly.
        /// </summary>
        public static DaleProject? FindProject(string? projectPath = null, string? startDirectory = null)
        {
            if (projectPath != null)
            {
                var fullPath = Path.GetFullPath(projectPath);
                if (!File.Exists(fullPath))
                {
                    return null;
                }

                return ParseCsproj(fullPath);
            }

            var dir = startDirectory ?? Directory.GetCurrentDirectory();

            while (dir != null)
            {
                var csprojFiles = Directory.GetFiles(dir, "*.csproj");
                foreach (var csproj in csprojFiles)
                {
                    var project = ParseCsproj(csproj);
                    if (project != null)
                    {
                        return project;
                    }
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        /// <summary>
        ///     Find the nearest .sln file, walking up from startDirectory.
        /// </summary>
        public static string? FindSolution(string? startDirectory = null)
        {
            var dir = startDirectory ?? Directory.GetCurrentDirectory();

            while (dir != null)
            {
                var slnFiles = Directory.GetFiles(dir, "*.sln");
                if (slnFiles.Length > 0)
                {
                    return slnFiles[0];
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        /// <summary>
        ///     Scan .cs files in the project directory for classes extending LogicBlockBase.
        ///     Used for code generation targeting (add timer, add serviceproperty).
        /// </summary>
        public static List<LogicBlockInfo> FindLogicBlocks(string projectDirectory)
        {
            var results = new List<LogicBlockInfo>();
            var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
            var pattern = new Regex(@"class\s+(\w+)\s*:\s*LogicBlockBase\b", RegexOptions.Compiled);

            foreach (var file in csFiles)
            {
                // Skip obj/bin directories
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                foreach (Match match in pattern.Matches(content))
                {
                    results.Add(new LogicBlockInfo
                                {
                                    ClassName = match.Groups[1].Value,
                                    FilePath = file,
                                });
                }
            }

            return results;
        }

        private static DaleProject? ParseCsproj(string csprojPath)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                var packageRefs = doc.Descendants(ns + "PackageReference").ToList();
                var sdkRef = packageRefs.FirstOrDefault(pr => string.Equals(pr.Attribute("Include")?.Value, "Vion.Dale.Sdk", StringComparison.OrdinalIgnoreCase));

                // Also check ProjectReference to Vion.Dale.Sdk
                if (sdkRef == null)
                {
                    var projectRefs = doc.Descendants(ns + "ProjectReference").ToList();
                    var sdkProjectRef = projectRefs.FirstOrDefault(pr => pr.Attribute("Include")?.Value?.Contains("Vion.Dale.Sdk") == true);

                    if (sdkProjectRef == null)
                    {
                        return null;
                    }

                    // ProjectReference found — SDK version unknown
                    return BuildProject(csprojPath, doc, ns, null);
                }

                var sdkVersion = sdkRef.Attribute("Version")?.Value;
                return BuildProject(csprojPath, doc, ns, sdkVersion);
            }
            catch
            {
                return null;
            }
        }

        private static DaleProject BuildProject(string csprojPath, XDocument doc, XNamespace ns, string? sdkVersion)
        {
            var props = doc.Descendants(ns + "PropertyGroup").SelectMany(pg => pg.Elements()).ToList();

            return new DaleProject
                   {
                       CsprojPath = csprojPath,
                       ProjectName = Path.GetFileNameWithoutExtension(csprojPath),
                       ProjectDirectory = Path.GetDirectoryName(csprojPath)!,
                       PackageId = props.FirstOrDefault(e => e.Name.LocalName == "PackageId")?.Value ?? Path.GetFileNameWithoutExtension(csprojPath),
                       Version = props.FirstOrDefault(e => e.Name.LocalName == "Version")?.Value,
                       SdkVersion = sdkVersion,
                       RootNamespace = props.FirstOrDefault(e => e.Name.LocalName == "RootNamespace")?.Value ?? Path.GetFileNameWithoutExtension(csprojPath),
                   };
        }
    }
}