using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Test.Build
{
    /// <summary>
    ///     Which projects package validation covers: docs/specs/_invariants.md § The published surface.
    ///     <para>
    ///         Every answer is the build system's own evaluation (<c>dotnet msbuild -getProperty</c>), never a
    ///         reading of the project files: whether a project packs is decided by MSBuild's defaults and its
    ///         packages' props as much as by what its file says, and a test project that never states it is
    ///         the case that matters — enabled there, restore looks for a baseline that was never published.
    ///     </para>
    /// </summary>
    [TestClass]
    public class PackageValidationShould
    {
        private const string BaselineProperty = "PackageValidationBaselineVersion=0.16.0";

        [TestMethod]
        [TestProperty("spec", "SYS-API-002")]
        public void EnableValidationForEveryPackableLibraryProjectGivenBaseline()
        {
            // Arrange
            var projects = SolutionProjects();

            // Act
            var evaluated = Evaluate(projects, BaselineProperty);

            // Assert
            var enabled = evaluated.Where(project => project.Value.EnablePackageValidation).Select(project => project.Key).Order(StringComparer.Ordinal).ToList();
            var packableLibraries = evaluated.Where(project => project.Value is { IsPackable: true, PackAsTool: false })
                                             .Select(project => project.Key)
                                             .Order(StringComparer.Ordinal)
                                             .ToList();
            Assert.Contains("Vion.Dale.Sdk", packableLibraries, "The evaluation found no packable library, so it compared nothing.");
            CollectionAssert.AreEqual(packableLibraries, enabled);
        }

        [TestMethod]
        [TestProperty("spec", "SYS-API-002")]
        public void LeaveValidationOffForProjectBaselineReleaseLacks()
        {
            // Arrange
            var projectPath = Path.Combine(RepositoryRoot(), "Vion.Dale.Sdk.MBus", "Vion.Dale.Sdk.MBus.csproj");

            // Act
            var evaluated = Evaluate(new[] { projectPath }, BaselineProperty, "DaleProjectsWithoutBaseline=Vion.Dale.Sdk.MBus.TestKit|Vion.Dale.Sdk.MBus");

            // Assert
            Assert.IsTrue(evaluated["Vion.Dale.Sdk.MBus"].IsPackable, "The project must be a packable library for its unvalidated state to mean anything.");
            Assert.IsFalse(evaluated["Vion.Dale.Sdk.MBus"].EnablePackageValidation);
        }

        [TestMethod]
        [TestProperty("spec", "SYS-API-002")]
        public void KeepValidationOnForProjectBaselineReleaseContains()
        {
            // Arrange — the one project named is the evaluated one's name with a suffix, so a substring
            // match would take it for the evaluated one
            var projectPath = Path.Combine(RepositoryRoot(), "Vion.Dale.Sdk", "Vion.Dale.Sdk.csproj");

            // Act
            var evaluated = Evaluate(new[] { projectPath }, BaselineProperty, "DaleProjectsWithoutBaseline=Vion.Dale.Sdk.MBus");

            // Assert
            Assert.IsTrue(evaluated["Vion.Dale.Sdk"].EnablePackageValidation);
        }

        [TestMethod]
        [TestProperty("spec", "SYS-API-002")]
        [DataRow("Vion.Dale.Sdk")]
        [DataRow("Vion.Dale.DevHost.Web")]
        public void LeaveValidationOffWithoutBaseline(string project)
        {
            // Arrange
            var projectPath = Path.Combine(RepositoryRoot(), project, project + ".csproj");

            // Act
            var evaluated = Evaluate(new[] { projectPath });

            // Assert
            Assert.IsTrue(evaluated[project].IsPackable, $"{project} must be a packable library for its unvalidated state to mean anything.");
            Assert.IsFalse(evaluated[project].EnablePackageValidation);
        }

        private static List<string> SolutionProjects()
        {
            var root = RepositoryRoot();
            return Regex.Matches(File.ReadAllText(Path.Combine(root, "Vion.Dale.Sdk.sln")), "\"([^\"]+\\.csproj)\"")
                        .Select(match => Path.Combine(root, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)))
                        .ToList();
        }

        /// <param name="properties">Global properties, each <c>Name=Value</c>.</param>
        private static Dictionary<string, EvaluatedProject> Evaluate(IReadOnlyCollection<string> projects, params string[] properties)
        {
            var results = new ConcurrentDictionary<string, EvaluatedProject>(StringComparer.Ordinal);
            Parallel.ForEach(projects,
                             // Each evaluation is a whole MSBuild process; a handful at a time keeps the suite's memory flat.
                             new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) },
                             project => results[Path.GetFileNameWithoutExtension(project)] = EvaluateOne(project, properties));
            return new Dictionary<string, EvaluatedProject>(results, StringComparer.Ordinal);
        }

        private static EvaluatedProject EvaluateOne(string project, string[] properties)
        {
            var startInfo = new ProcessStartInfo("dotnet")
                            {
                                WorkingDirectory = RepositoryRoot(),
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            };

            var arguments = new List<string> { "msbuild", project, "-nologo", "-getProperty:IsPackable", "-getProperty:PackAsTool", "-getProperty:EnablePackageValidation" };
            arguments.AddRange(properties.Select(property => "-p:" + property));

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            Assert.IsNotNull(process, "Could not start MSBuild.");

            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, $"Evaluating {project} failed: {output}{error.GetAwaiter().GetResult()}");

            var evaluated = JsonDocument.Parse(output).RootElement.GetProperty("Properties");
            return new EvaluatedProject(IsTrue(evaluated, "IsPackable"), IsTrue(evaluated, "PackAsTool"), IsTrue(evaluated, "EnablePackageValidation"));
        }

        private static bool IsTrue(JsonElement properties, string name)
        {
            return string.Equals(properties.GetProperty(name).GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Vion.Dale.Sdk.sln")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root above the test output directory.");
            return directory.FullName;
        }

        private readonly record struct EvaluatedProject(bool IsPackable, bool PackAsTool, bool EnablePackageValidation);
    }
}
