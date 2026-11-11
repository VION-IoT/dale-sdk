using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;

namespace Vion.Dale.Sdk.Test.Build
{
    /// <summary>
    ///     What <c>Vion.Dale.Sdk/build/Vion.Dale.Sdk.targets</c> does to a library project that imports it:
    ///     docs/specs/introspection.md § The declared minimum Dale SDK.
    ///     <para>
    ///         It shells out to <c>dotnet build</c> because the MSBuild evaluation and the .NET SDK's assembly
    ///         info generation are what is under test. The library is a throwaway project in a temp directory,
    ///         outside this repository, so no child build writes into an output another build reads. It removes
    ///         the analyzer the targets file adds: that path exists only inside the packed package.
    ///     </para>
    /// </summary>
    [TestClass]
    public class SdkTargetsShould
    {
        private string _projectDirectory = null!;

        [TestInitialize]
        public void Initialize()
        {
            _projectDirectory = Directory.CreateTempSubdirectory("dale-sdk-targets-").FullName;
        }

        [TestCleanup]
        public void Cleanup()
        {
            // The built library is read in a collectible load context and unloaded, but an unload is not
            // immediate; a directory the runtime still holds is left behind rather than failing the test.
            try
            {
                Directory.Delete(_projectDirectory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-018.1")]
        [DataRow("0.14.3")]
        [DataRow("1.2.0")]
        public void CompileDeclaredMinimumIntoAssemblyMetadata(string declared)
        {
            // Arrange
            var project = WriteLibraryProject(declared);

            // Act
            var build = BuildLibrary(project);

            // Assert
            Assert.AreEqual(0, build.ExitCode, build.Output);
            Assert.AreEqual(declared, ReadAssemblyMetadata(build.AssemblyPath, "MinimumDaleSdkVersion"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-018.1")]
        public void CompileNoMinimumWhenNoneDeclared()
        {
            // Arrange
            var project = WriteLibraryProject(null);

            // Act
            var build = BuildLibrary(project);

            // Assert
            Assert.AreEqual(0, build.ExitCode, build.Output);
            Assert.IsNull(ReadAssemblyMetadata(build.AssemblyPath, "MinimumDaleSdkVersion"));
        }

        private string WriteLibraryProject(string? minimumDaleSdkVersion)
        {
            var targets = Path.Combine(RepositoryRoot(), "Vion.Dale.Sdk", "build", "Vion.Dale.Sdk.targets");
            var properties = new XElement("PropertyGroup", new XElement("TargetFramework", "netstandard2.1"), new XElement("AssemblyName", "Library"));
            if (minimumDaleSdkVersion != null)
            {
                properties.Add(new XElement("MinimumDaleSdkVersion", minimumDaleSdkVersion));
            }

            var project = Path.Combine(_projectDirectory, "Library.csproj");
            new XElement("Project",
                         new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                         properties,
                         new XElement("Import", new XAttribute("Project", targets)),
                         new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Remove", "@(Analyzer)")))).Save(project);
            File.WriteAllText(Path.Combine(_projectDirectory, "Library.cs"), "public class Library { }");
            return project;
        }

        private BuildResult BuildLibrary(string project)
        {
            var output = Path.Combine(_projectDirectory, "out");
            var startInfo = new ProcessStartInfo("dotnet")
                            {
                                WorkingDirectory = _projectDirectory,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            };

            foreach (var argument in new[] { "build", project, "-nologo", "-v:q", "-nodereuse:false", "-o", output })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            Assert.IsNotNull(process, "Could not start dotnet build.");

            var error = process.StandardError.ReadToEndAsync();
            var text = new StringBuilder(process.StandardOutput.ReadToEnd());
            text.Append(error.GetAwaiter().GetResult());
            process.WaitForExit();

            return new BuildResult(process.ExitCode, text.ToString(), Path.Combine(output, "Library.dll"));
        }

        private static string? ReadAssemblyMetadata(string assemblyPath, string key)
        {
            var context = new AssemblyLoadContext(null, true);
            try
            {
                using var stream = File.OpenRead(assemblyPath);
                return context.LoadFromStream(stream).GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;
            }
            finally
            {
                context.Unload();
            }
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

        private readonly record struct BuildResult(int ExitCode, string Output, string AssemblyPath);
    }
}
