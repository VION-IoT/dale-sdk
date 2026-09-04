using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
    ///         compilation of the real project, not a compilation this project assembles. The proof is that
    ///         compilation and never where its output lands, so every output of the whole dependency graph is
    ///         sent to a disposable directory. Release 0.11.1 is what happens without that: the child build
    ///         carries no <c>/p:Version</c>, CI runs the tests between the stamped build and the pack, and the
    ///         three <c>lib</c> assemblies of this probe's build graph shipped stamped <c>0.0.0.0</c> while
    ///         their nuspecs said <c>0.11.1</c> — every consumer of them died at startup.
    ///         <see cref="LeaveBuildOutputsOfDependencyGraphUntouched" /> is the standing guard.
    ///     </para>
    /// </summary>
    [TestClass]
    public class AnalyzerWiringShould
    {
        /// <summary>A version no other build in this repository uses, so the guard's child builds always recompile.</summary>
        private const string WiringGuardVersion = "0.0.0-analyzer-wiring-guard";

        /// <summary>
        ///     Every project the probe builds reach: the two under test, plus what they pull in by
        ///     <c>ProjectReference</c>. That set is exactly the blast radius of the 0.11.1 clobber.
        /// </summary>
        private static readonly string[] ProbeBuildGraph =
        [
            "Vion.Dale.Sdk",
            "Vion.Dale.Sdk.Generators",
            "Vion.Dale.Sdk.DigitalIo",
            "Vion.Dale.Sdk.AnalogIo",
        ];

        private static readonly string[] ProbedProjects = ["Vion.Dale.Sdk.DigitalIo", "Vion.Dale.Sdk.AnalogIo"];

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-017.4")]
        [DataRow("Vion.Dale.Sdk.DigitalIo")]
        [DataRow("Vion.Dale.Sdk.AnalogIo")]
        public void RunDaleAnalyzersOverIoProjects(string projectName)
        {
            // Arrange / Act / Assert
            var project = ProjectFile(projectName);
            Assert.IsTrue(File.Exists(project), $"Project not found: {project}");

            var (exitCode, output) = Build(project);

            Assert.AreNotEqual(0, exitCode, $"The probe build of {projectName} succeeded, so the Dale analyzers did not run over it.\n{output}");
            Assert.Contains("DALE046", output, $"The probe build of {projectName} failed for some other reason than DALE046.\n{output}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-018.4")]
        [DataRow("Vion.Dale.Sdk.DigitalIo")]
        [DataRow("Vion.Dale.Sdk.AnalogIo")]
        public void KeepProbeOutOfOrdinaryBuild(string projectName)
        {
            // Arrange / Act / Assert
            // The probe is only a gate as long as it is invisible the rest of the time — a build without the
            // property must not see it, or every build of the SDK would fail.
            var (exitCode, output) = Build(ProjectFile(projectName), false);

            Assert.AreEqual(0, exitCode, $"An ordinary build of {projectName} must not compile the analyzer-wiring probe.\n{output}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-018.4")]
        public void LeaveBuildOutputsOfDependencyGraphUntouched()
        {
            // Arrange / Act / Assert
            // The 0.11.1 regression, pinned. CI builds the solution stamped, runs the tests, then packs the
            // Release outputs it already built — so a test that shells an unstamped `dotnet build` of any
            // project in this graph replaces those outputs with 0.0.0.0 ones between the stamp and the pack.
            //
            // Compare bytes, not versions: locally nothing is stamped (Directory.Build.props falls back to
            // 0.0.0-local), so a clobbered assembly and an intact one carry the same 0.0.0.0 and a version
            // comparison would pass here while CI went on shipping the wrong bytes. That asymmetry is exactly
            // why the clobber stayed invisible until the packages were on nuget.org. Assembly fingerprints
            // carry the version as well, so a failure still names the stamp that was lost.
            //
            // The version passed here is what makes the guard bite everywhere. What triggered the clobber in
            // CI was the child build's inputs DIFFERING from the stamped build's: a same-inputs child build is
            // up to date and rewrites nothing, which is how an unisolated build looks innocent locally. Naming
            // a version no other build uses forces the recompile the guard has to survive.
            var before = FingerprintProbeBuildGraph();

            foreach (var projectName in ProbedProjects)
            {
                Build(ProjectFile(projectName), version: WiringGuardVersion);
                Build(ProjectFile(projectName), false, WiringGuardVersion);
            }

            var after = FingerprintProbeBuildGraph();

            var disturbed = after.Where(entry => !before.TryGetValue(entry.Key, out var fingerprint) || fingerprint != entry.Value)
                                 .Select(entry => $"{entry.Key}\n    before: {(before.TryGetValue(entry.Key, out var was) ? was : "(did not exist)")}\n    after:  {entry.Value}")
                                 .Concat(before.Keys.Where(path => !after.ContainsKey(path)).Select(path => $"{path}\n    deleted"))
                                 .OrderBy(line => line, StringComparer.Ordinal)
                                 .ToList();

            Assert.IsEmpty(disturbed,
                           "The analyzer-wiring builds wrote into the repository's own build outputs. Those are what `dotnet pack` ships, " +
                           "and these builds carry no /p:Version — this is how 0.11.1 shipped lib assemblies stamped 0.0.0.0. Send the " +
                           $"child build somewhere disposable instead.\n{string.Join("\n", disturbed)}");
        }

        private static (int ExitCode, string Output) Build(string projectPath, bool withProbe = true, string? version = null)
        {
            var configuration = typeof(AnalyzerWiringShould).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

            // Everything this build produces — for the project and for its whole ProjectReference graph — lands
            // here and is thrown away. BaseIntermediateOutputPath is deliberately NOT moved: NuGet reads
            // project.assets.json from it, so redirecting it fails a --no-restore build with NETSDK1004. The
            // (non-Base) IntermediateOutputPath moves the compile while leaving the restore artefacts in place.
            var scratch = Path.Combine(Path.GetTempPath(), "dale-analyzer-wiring", Guid.NewGuid().ToString("N"));

            var arguments = new List<string>
                            {
                                "build", projectPath, "-c", configuration, "--no-restore", "-nodereuse:false", "--nologo", "-v", "q",
                                "-p:BaseOutputPath=" + MsBuildDirectory(scratch, "bin"),
                                "-p:IntermediateOutputPath=" + MsBuildDirectory(scratch, "obj"),
                            };

            if (withProbe)
            {
                arguments.Add("-p:DaleAnalyzerWiringProbe=true");
            }

            if (version is not null)
            {
                arguments.Add("-p:Version=" + version);
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

            try
            {
                using var process = Process.Start(startInfo);
                Assert.IsNotNull(process, "Could not start dotnet build.");

                var output = new StringBuilder();
                output.Append(process.StandardOutput.ReadToEnd());
                output.Append(process.StandardError.ReadToEnd());
                process.WaitForExit();

                return (process.ExitCode, output.ToString());
            }
            finally
            {
                Discard(scratch);
            }
        }

        /// <summary>
        ///     An MSBuild directory property value: forward slashes and a trailing separator, which MSBuild
        ///     normalises on every OS. A trailing backslash would escape the closing quote of the argument
        ///     whenever the temp path contains a space.
        /// </summary>
        private static string MsBuildDirectory(string scratch, string leaf)
        {
            return Path.Combine(scratch, leaf).Replace('\\', '/') + '/';
        }

        private static void Discard(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // A leftover scratch directory under the temp path is harmless; failing the test over it is not.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static Dictionary<string, string> FingerprintProbeBuildGraph()
        {
            var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);

            var outputDirectories = ProbeBuildGraph.SelectMany(project => new[] { "bin", "obj" }.Select(output => Path.Combine(RepositoryRoot(), project, output)))
                                                   .Where(Directory.Exists);

            foreach (var file in outputDirectories.SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)))
            {
                fingerprints[file] = Fingerprint(file);
            }

            return fingerprints;
        }

        private static string Fingerprint(string file)
        {
            using var stream = File.OpenRead(file);
            var content = Convert.ToHexString(SHA256.HashData(stream))[..16];

            return file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? $"sha={content} assemblyVersion={AssemblyVersionOf(file)}" : $"sha={content}";
        }

        private static string AssemblyVersionOf(string file)
        {
            try
            {
                return AssemblyName.GetAssemblyName(file).Version?.ToString() ?? "none";
            }
            catch (BadImageFormatException)
            {
                return "not a managed assembly";
            }
        }

        private static string ProjectFile(string projectName)
        {
            return Path.Combine(RepositoryRoot(), projectName, projectName + ".csproj");
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