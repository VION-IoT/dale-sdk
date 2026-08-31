using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     The analyzers are shipped inside <c>Vion.Dale.Sdk</c> and referenced by the SDK's own projects with
    ///     <c>OutputItemType="Analyzer"</c>. An analyzer can pass every test in this project and still be
    ///     completely absent from a given project's compilation — that is how the I/O projects went unjudged
    ///     until #154 (sdk-surface-conventions § 5, "Known non-conforming code").
    ///     <para>
    ///         Verifying the reference is live used to mean breaking a real <c>[ScenarioWire]</c> declaration by
    ///         hand and remembering to revert it. Instead one committed probe — <c>AnalyzerWiring/</c>, beside
    ///         this test — holds an invalid declaration, and each I/O project links it in only under
    ///         <c>-p:DaleAnalyzerWiringProbe=true</c>. This test runs that build and requires DALE046 to fail
    ///         it. Remove the analyzer reference and this test goes red. The probe is nobody's source file by
    ///         default, so no shipped project carries one that exists only to fail.
    ///     </para>
    ///     <para>
    ///         It shells out to <c>dotnet build</c> because that is the thing under test: the real MSBuild
    ///         compilation of the real project, not a compilation this project assembles. The build runs in
    ///         place and fails at compile, so no output assembly is overwritten.
    ///     </para>
    /// </summary>
    [TestClass]
    public class AnalyzerWiringShould
    {
        [TestMethod]
        [DataRow("Vion.Dale.Sdk.DigitalIo")]
        [DataRow("Vion.Dale.Sdk.AnalogIo")]
        public void RunTheDaleAnalyzersOverTheIoProjects(string projectName)
        {
            var project = Path.Combine(RepositoryRoot(), projectName, projectName + ".csproj");
            Assert.IsTrue(File.Exists(project), $"Project not found: {project}");

            var (exitCode, output) = Build(project);

            Assert.AreNotEqual(0, exitCode, $"The probe build of {projectName} succeeded, so the Dale analyzers did not run over it.\n{output}");
            Assert.Contains("DALE046", output, $"The probe build of {projectName} failed for some other reason than DALE046.\n{output}");
        }

        [TestMethod]
        [DataRow("Vion.Dale.Sdk.DigitalIo")]
        [DataRow("Vion.Dale.Sdk.AnalogIo")]
        public void KeepTheProbeOutOfAnOrdinaryBuild(string projectName)
        {
            // The probe is only a gate as long as it is invisible the rest of the time — a build without the
            // property must not see it, or every build of the SDK would fail.
            var project = Path.Combine(RepositoryRoot(), projectName, projectName + ".csproj");

            var (exitCode, output) = Build(project, false);

            Assert.AreEqual(0, exitCode, $"An ordinary build of {projectName} must not compile the analyzer-wiring probe.\n{output}");
        }

        private static (int ExitCode, string Output) Build(string projectPath, bool withProbe = true)
        {
            var configuration = typeof(AnalyzerWiringShould).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

            var arguments = new List<string> { "build", projectPath, "-c", configuration, "--no-restore", "-nodereuse:false", "--nologo", "-v", "q" };
            if (withProbe)
            {
                arguments.Add("-p:DaleAnalyzerWiringProbe=true");
            }

            var startInfo = new ProcessStartInfo("dotnet")
                            {
                                WorkingDirectory = RepositoryRoot(),
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            Assert.IsNotNull(process, "Could not start dotnet build.");

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());
            process.WaitForExit();

            return (process.ExitCode, output.ToString());
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Vion.Dale.Sdk.sln")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root (no Vion.Dale.Sdk.sln above the test output directory).");
            return directory.FullName;
        }
    }
}