using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

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
    ///         It shells out to MSBuild because that is the thing under test: the real MSBuild compilation of the
    ///         real project, not a compilation this project assembles. The proof is that compilation and never
    ///         where its output lands, so every output of the whole dependency graph is sent to a disposable
    ///         directory. Release 0.11.1 is what happens without that: the child build carries no
    ///         <c>/p:Version</c>, CI runs the tests between the stamped build and the pack, and the three
    ///         <c>lib</c> assemblies of this probe's build graph shipped stamped <c>0.0.0.0</c> while their
    ///         nuspecs said <c>0.11.1</c> — every consumer of them died at startup.
    ///         <see cref="LeaveBuildOutputsOfDependencyGraphUntouched" /> is the standing guard.
    ///     </para>
    ///     <para>
    ///         Every build but the guard's runs once, before the first test: one MSBuild invocation per set of
    ///         global properties builds all of that set's projects, so the dependency graph they share builds once
    ///         per set rather than once per project. A diagnostic is judged by the project MSBuild attributes it to,
    ///         the <c>[path.csproj]</c> suffix every diagnostic line carries. Because those builds are the system
    ///         under test and run in <c>[ClassInitialize]</c>, a test's <c>// Act</c> reads its set's result rather
    ///         than starting the build itself (testing-conventions § 13).
    ///     </para>
    /// </summary>
    [TestClass]
    public class AnalyzerWiringShould
    {
        /// <summary>A version no other build in this repository uses, so the guard's child builds always recompile.</summary>
        private const string WiringGuardVersion = "0.0.0-analyzer-wiring-guard";

        /// <summary>The HTTP package, whose project name and declared published namespace are the same string.</summary>
        private const string HttpPackage = "Vion.Dale.Sdk.Http";

        private const string IoProbeProperty = "DaleAnalyzerWiringProbe";

        private const string HttpProbeProperty = "DaleHttpAnalyzerWiringProbe";

        private const string ModbusProbeProperty = "DaleModbusAnalyzerWiringProbe";

        private const string TestKitProbeProperty = "DaleTestKitAnalyzerWiringProbe";

        /// <summary>
        ///     The three Modbus packages and the published namespace each declares. Until the analyzer
        ///     reference landed beside it, none of the three was judged by any DALE diagnostic at all, and
        ///     <c>Vion.Dale.Sdk.Modbus.Core</c> declared no published namespace either — so arming it without
        ///     a declaration would have asked about nothing.
        /// </summary>
        private static readonly (string Project, string Namespace)[] ProbedModbusPackages =
        [
            ("Vion.Dale.Sdk.Modbus.Core", "Vion.Dale.Sdk.Modbus.Core"),
            ("Vion.Dale.Sdk.Modbus.Rtu", "Vion.Dale.Sdk.Modbus.Rtu"),
            ("Vion.Dale.Sdk.Modbus.Tcp", "Vion.Dale.Sdk.Modbus.Tcp"),
        ];

        /// <summary>
        ///     Every project <see cref="LeaveBuildOutputsOfDependencyGraphUntouched" />'s own builds reach:
        ///     the two I/O projects it probes, plus what they pull in by <c>ProjectReference</c>. That set is
        ///     exactly the blast radius of the 0.11.1 clobber. The other probed packages are not in it — they
        ///     run through the same <see cref="Build" /> helper and its scratch redirect, which is what the
        ///     guard proves; nothing here scans their outputs.
        /// </summary>
        private static readonly string[] ProbeBuildGraph =
        [
            "Vion.Dale.Sdk",
            "Vion.Dale.Sdk.Generators",
            "Vion.Dale.Sdk.DigitalIo",
            "Vion.Dale.Sdk.AnalogIo",
        ];

        private static readonly string[] ProbedProjects = ["Vion.Dale.Sdk.DigitalIo", "Vion.Dale.Sdk.AnalogIo"];

        /// <summary>
        ///     The six test kits and the published namespace each declares. Every one of them declares
        ///     <c>[assembly: PublicApiNamespace]</c>, and until the analyzer reference landed beside it none
        ///     of those declarations had a reader.
        /// </summary>
        private static readonly (string Project, string Namespace)[] ProbedKits =
        [
            ("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.TestKit"),
            ("Vion.Dale.Sdk.DigitalIo.TestKit", "Vion.Dale.Sdk.DigitalIo.TestKit"),
            ("Vion.Dale.Sdk.AnalogIo.TestKit", "Vion.Dale.Sdk.AnalogIo.TestKit"),
            ("Vion.Dale.Sdk.Modbus.Rtu.TestKit", "Vion.Dale.Sdk.Modbus.Rtu.TestKit"),
            ("Vion.Dale.Sdk.Modbus.Tcp.TestKit", "Vion.Dale.Sdk.Modbus.Tcp.TestKit"),
            ("Vion.Dale.Sdk.Http.TestKit", "Vion.Dale.Sdk.Http.TestKit"),
        ];

        private static readonly BuildSet IoProbe = new(nameof(IoProbe), IoProbeProperty, null, ProbedProjects);

        private static readonly BuildSet TestKitProbe = new(nameof(TestKitProbe), TestKitProbeProperty, null, ProbedKits.Select(kit => kit.Project).ToArray());

        private static readonly BuildSet ModbusProbe = new(nameof(ModbusProbe), ModbusProbeProperty, null, ProbedModbusPackages.Select(package => package.Project).ToArray());

        private static readonly BuildSet HttpProbe = new(nameof(HttpProbe), HttpProbeProperty, null, [HttpPackage]);

        /// <summary>Every probed project with no probe property set: the build a consumer of the SDK gets.</summary>
        private static readonly BuildSet Ordinary = new(nameof(Ordinary),
                                                        null,
                                                        null,
                                                        ProbedProjects.Concat(TestKitProbe.Projects).Concat(ModbusProbe.Projects).Append(HttpPackage).ToArray());

        /// <summary>
        ///     The probe half of the builds <see cref="LeaveBuildOutputsOfDependencyGraphUntouched" /> runs, stamped with a
        ///     version no other build uses. What triggered the clobber in CI was the child build's inputs
        ///     DIFFERING from the stamped build's: a same-inputs child build is up to date and rewrites nothing,
        ///     which is how an unisolated build looks innocent locally. Naming a version no other build uses
        ///     forces the recompile the guard has to survive.
        /// </summary>
        private static readonly BuildSet GuardProbe = new(nameof(GuardProbe), IoProbeProperty, WiringGuardVersion, ProbedProjects);

        /// <summary>The ordinary half of the guard's builds, stamped like <see cref="GuardProbe" />.</summary>
        private static readonly BuildSet GuardOrdinary = new(nameof(GuardOrdinary), null, WiringGuardVersion, ProbedProjects);

        private static readonly Dictionary<string, BuildResult> Results = new(StringComparer.Ordinal);

        private static string _scratch = null!;

        [ClassInitialize]
        public static void BuildEverySet(TestContext context)
        {
            // Everything these builds produce — for the projects and for their whole ProjectReference graph —
            // lands under here and is thrown away after the last test.
            _scratch = Path.Combine(Path.GetTempPath(), "dale-analyzer-wiring", Guid.NewGuid().ToString("N"));

            foreach (var set in new[] { IoProbe, TestKitProbe, ModbusProbe, HttpProbe, Ordinary })
            {
                Results[set.Name] = Build(set);
            }
        }

        [ClassCleanup]
        public static void DiscardScratch()
        {
            try
            {
                if (Directory.Exists(_scratch))
                {
                    Directory.Delete(_scratch, true);
                }
            }
            catch (IOException)
            {
                // A leftover scratch directory under the temp path is harmless; failing the run over it is not.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-017.4")]
        [DataRow("Vion.Dale.Sdk.DigitalIo")]
        [DataRow("Vion.Dale.Sdk.AnalogIo")]
        public void RunDaleAnalyzersOverIoProjects(string projectName)
        {
            // Arrange / Act
            var build = Results[IoProbe.Name];

            // Assert
            // The set's exit code is not asserted: both projects share it, so one project's DALE046 would pass the
            // other project's row.
            Assert.IsTrue(build.LinesOf(projectName).Any(line => line.Contains("error DALE046")),
                          $"The probe build of {projectName} reported no DALE046, so the Dale analyzers did not fail it.\n{build.Output}");
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
            AssertOrdinaryBuildOf(projectName, "DALE046");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.2")]
        [DataRow("Vion.Dale.Sdk.TestKit")]
        [DataRow("Vion.Dale.Sdk.DigitalIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.AnalogIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp.TestKit")]
        [DataRow("Vion.Dale.Sdk.Http.TestKit")]
        public void RunDaleAnalyzersOverTestKits(string projectName)
        {
            // Arrange
            // DALE014 is a warning, not an error, so unlike the I/O probe above this build SUCCEEDS and the
            // proof is the diagnostic it emitted — asserting a non-zero exit code here would pass on any
            // broken build and fail on a working analyzer.
            var declaredNamespace = ProbedKits.Single(kit => kit.Project == projectName).Namespace;

            // Act
            var build = Results[TestKitProbe.Name];

            // Assert
            AssertDale014InOwnNamespace(build, projectName, declaredNamespace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-013.2")]
        public void RunDaleAnalyzersOverHttpPackage()
        {
            // Arrange
            // Like the kits above and unlike the I/O probe, DALE014 is a warning, so this build SUCCEEDS and
            // the proof is the diagnostic it emitted. Until the reference landed beside it, three of the five
            // public types this package ships carried no surface mark and nothing said so.

            // Act
            var build = Results[HttpProbe.Name];

            // Assert
            AssertDale014InOwnNamespace(build, HttpPackage, HttpPackage);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-013.2")]
        public void KeepHttpProbeOutOfOrdinaryBuild()
        {
            // Arrange / Act / Assert — the probe is only a gate as long as it is invisible the rest of the time
            AssertOrdinaryBuildOf(HttpPackage, "DALE014");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.2")]
        [DataRow("Vion.Dale.Sdk.Modbus.Core")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp")]
        public void RunDaleAnalyzersOverModbusPackages(string projectName)
        {
            // Arrange
            // Like the kits and the HTTP package and unlike the I/O probe, DALE014 is a warning, so this
            // build SUCCEEDS and the proof is the diagnostic it emitted. The namespace assertion is what
            // separates "the analyzer ran" from "the analyzer ran and this package's own declaration was
            // the reason": Core declared none at all, so an armed analyzer there judged nothing.
            var declaredNamespace = ProbedModbusPackages.Single(package => package.Project == projectName).Namespace;

            // Act
            var build = Results[ModbusProbe.Name];

            // Assert
            AssertDale014InOwnNamespace(build, projectName, declaredNamespace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.2")]
        [DataRow("Vion.Dale.Sdk.Modbus.Core")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp")]
        public void KeepModbusProbeOutOfOrdinaryBuild(string projectName)
        {
            // Arrange / Act / Assert - the probe is only a gate as long as it is invisible the rest of the time, and an
            // ordinary build of these three carries no DALE014 of its own now that every public type of all
            // three is marked. Both halves matter: a leaked probe and an unmarked type look the same here.
            AssertOrdinaryBuildOf(projectName, "DALE014");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.2")]
        [DataRow("Vion.Dale.Sdk.TestKit")]
        [DataRow("Vion.Dale.Sdk.DigitalIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.AnalogIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp.TestKit")]
        [DataRow("Vion.Dale.Sdk.Http.TestKit")]
        public void KeepTestKitProbeOutOfOrdinaryBuild(string projectName)
        {
            // Arrange / Act / Assert — the probe is only a gate as long as it is invisible the rest of the time
            AssertOrdinaryBuildOf(projectName, "DALE014");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-018.4")]
        public void LeaveBuildOutputsOfDependencyGraphUntouched()
        {
            // Arrange
            // The 0.11.1 regression, pinned. CI builds the solution stamped, runs the tests, then packs the
            // Release outputs it already built — so a test that shells an unstamped build of any project in
            // this graph replaces those outputs with 0.0.0.0 ones between the stamp and the pack.
            //
            // The guard's builds carry a version nothing else builds with, and what is judged is whether that
            // version reached the repository's outputs. A byte comparison of the outputs before and after cannot
            // tell these builds' writes from those of a `dotnet test` of the solution that builds the same
            // projects while the tests run; no other build can write this version.
            var stamp = Encoding.UTF8.GetBytes(WiringGuardVersion);

            // Act
            var guardProbe = Build(GuardProbe);
            var guardOrdinary = Build(GuardOrdinary);

            // Assert
            // Each half shows in its own way that it reached the compile: the probe half by the DALE046 it fails
            // on, the ordinary half by the assemblies it produced. Those assemblies carrying the stamp is what
            // makes its absence from the repository mean something.
            Assert.IsTrue(ProbedProjects.All(project => guardProbe.LinesOf(project).Any(line => line.Contains("error DALE046"))),
                          $"The guard's probe build did not reach the compile of both probed projects.\n{guardProbe.Output}");
            Assert.IsTrue(ProbedProjects.All(guardOrdinary.Built),
                          $"The guard's ordinary build did not produce the probed projects, so there was nothing to guard.\n{guardOrdinary.Output}");
            Assert.IsNotEmpty(FilesCarrying(stamp, [guardOrdinary.OutputDirectory]),
                              $"The guard's own outputs do not carry {WiringGuardVersion}, so its absence from the repository proves nothing.");

            var leaked = FilesCarrying(stamp, ProbeBuildGraph.SelectMany(project => new[] { "bin", "obj" }.Select(output => Path.Combine(RepositoryRoot(), project, output))));

            Assert.IsEmpty(leaked,
                           "The analyzer-wiring builds wrote into the repository's own build outputs. Those are what `dotnet pack` ships, " +
                           "and a child build carries none of CI's /p:Version — this is how 0.11.1 shipped lib assemblies stamped 0.0.0.0. Send " +
                           "the child build somewhere disposable instead. A file left by an earlier leaking run stays until its project is " +
                           $"rebuilt.\n{string.Join("\n", leaked)}");
        }

        private static void AssertDale014InOwnNamespace(BuildResult build, string projectName, string declaredNamespace)
        {
            var lines = build.LinesOf(projectName).Where(line => line.Contains("DALE014")).ToList();

            Assert.IsNotEmpty(lines, $"The probe build of {projectName} drew no DALE014, so the Dale analyzers did not run over it.{Environment.NewLine}{build.Output}");
            Assert.IsTrue(lines.Any(line => line.Contains($"in namespace '{declaredNamespace}'")),
                          $"The DALE014 in the probe build of {projectName} named another namespace.{Environment.NewLine}{build.Output}");
        }

        /// <summary>
        ///     The ordinary build attributes no error and no <paramref name="diagnostic" /> to this project, and produced it.
        ///     Judged per project, so a probe leaking into one project fails that project's test and not every other one
        ///     sharing the build. The last check is for a project whose dependency failed: it never compiles, so it logs
        ///     nothing of its own and would otherwise pass on silence. That lines are attributed at all is proven by the
        ///     <c>RunDaleAnalyzers…</c> tests, which read the same lines and fail on none.
        /// </summary>
        private static void AssertOrdinaryBuildOf(string projectName, string diagnostic)
        {
            var build = Results[Ordinary.Name];
            var lines = build.LinesOf(projectName).ToList();

            Assert.IsFalse(lines.Any(line => line.Contains(": error ")), $"An ordinary build of {projectName} must succeed.{Environment.NewLine}{build.Output}");
            Assert.IsFalse(lines.Any(line => line.Contains(diagnostic)),
                           $"An ordinary build of {projectName} must not compile the analyzer-wiring probe.{Environment.NewLine}{build.Output}");
            Assert.IsTrue(build.Built(projectName), $"The ordinary build did not produce {projectName}.{Environment.NewLine}{build.Output}");
        }

        private static BuildResult Build(BuildSet set)
        {
            var configuration = typeof(AnalyzerWiringShould).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
            var directory = Path.Combine(_scratch, set.Name);
            Directory.CreateDirectory(directory);

            var projectPaths = set.Projects.ToDictionary(project => project, ProjectFile, StringComparer.Ordinal);

            // One traversal per set, so the projects that share a dependency graph build it once. Every set's
            // projects share one output directory: their assemblies are named for their projects, and a
            // dependency they share builds once per invocation.
            var traversal = Path.Combine(directory, "wiring.proj");

            // The paths go into an MSBuild item list, so the characters MSBuild reads there are escaped; XElement
            // escapes what XML reads.
            var projects = string.Join(";", projectPaths.Values.Select(EscapeForMsBuild));
            new XElement("Project",
                         new XElement("Target",
                                      new XAttribute("Name", "Build"),
                                      new XElement("MSBuild", new XAttribute("Projects", projects), new XAttribute("Targets", "Build"), new XAttribute("BuildInParallel", "true"))))
                .Save(traversal);

            // BaseIntermediateOutputPath is deliberately NOT moved: NuGet reads project.assets.json from it, so
            // redirecting it fails a build with no restore with NETSDK1004. The (non-Base) IntermediateOutputPath
            // moves the compile while leaving the restore artefacts in place. No -restore, for the same reason.
            var arguments = new List<string>
                            {
                                "msbuild", traversal, "-m", "-nodereuse:false", "-nologo", "-v:q",
                                "-p:Configuration=" + configuration,
                                "-p:BaseOutputPath=" + MsBuildDirectory(directory, "bin"),
                                "-p:IntermediateOutputPath=" + MsBuildDirectory(directory, "obj"),
                            };

            if (set.ProbeProperty is not null)
            {
                arguments.Add($"-p:{set.ProbeProperty}=true");
            }

            if (set.Version is not null)
            {
                arguments.Add("-p:Version=" + set.Version);
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
            Assert.IsNotNull(process, "Could not start MSBuild.");

            var error = process.StandardError.ReadToEndAsync();
            var output = new StringBuilder(process.StandardOutput.ReadToEnd());
            output.Append(error.GetAwaiter().GetResult());
            process.WaitForExit();

            return new BuildResult(process.ExitCode, output.ToString(), projectPaths, Path.Combine(directory, "bin"));
        }

        /// <summary>A value MSBuild reads literally, in an item list or a <c>-p:</c> switch, whatever path it names.</summary>
        private static string EscapeForMsBuild(string value)
        {
            return value.Replace("%", "%25")
                        .Replace(";", "%3B")
                        .Replace(",", "%2C")
                        .Replace("$", "%24")
                        .Replace("@", "%40")
                        .Replace("'", "%27")
                        .Replace("*", "%2A")
                        .Replace("?", "%3F");
        }

        /// <summary>
        ///     An MSBuild directory property value: forward slashes and a trailing separator, which MSBuild
        ///     normalises on every OS. A trailing backslash would escape the closing quote of the argument
        ///     whenever the temp path contains a space.
        /// </summary>
        private static string MsBuildDirectory(string scratch, string leaf)
        {
            return EscapeForMsBuild(Path.Combine(scratch, leaf).Replace('\\', '/')) + '/';
        }

        /// <summary>
        ///     Every file under <paramref name="directories" /> whose bytes contain <paramref name="stamp" />: an assembly
        ///     carries its informational version as UTF-8, and the generated <c>AssemblyInfo.cs</c> carries it as text.
        /// </summary>
        private static List<string> FilesCarrying(byte[] stamp, IEnumerable<string> directories)
        {
            return directories.Where(Directory.Exists)
                              .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                              .Where(file => File.ReadAllBytes(file).AsSpan().IndexOf(stamp) >= 0)
                              .OrderBy(file => file, StringComparer.Ordinal)
                              .ToList();
        }

        private static string ProjectFile(string projectName)
        {
            var project = Path.Combine(RepositoryRoot(), projectName, projectName + ".csproj");
            Assert.IsTrue(File.Exists(project), $"Project not found: {project}");
            return project;
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

        /// <summary>One MSBuild invocation: the global properties it sets and the projects it builds under them.</summary>
        private sealed record BuildSet(string Name, string? ProbeProperty, string? Version, string[] Projects);

        private sealed record BuildResult(int ExitCode, string Output, Dictionary<string, string> ProjectPaths, string OutputDirectory)
        {
            /// <summary>
            ///     The lines MSBuild attributes to <paramref name="projectName" />: each diagnostic ends in
            ///     <c>[path.csproj]</c>, or <c>[path.csproj::TargetFramework=…]</c> for a multi-targeted project.
            /// </summary>
            public IEnumerable<string> LinesOf(string projectName)
            {
                var path = ProjectPaths[projectName];
                return Output.Split('\n').Where(line => line.Contains($"[{path}]") || line.Contains($"[{path}::"));
            }

            public bool Built(string projectName)
            {
                return Directory.Exists(OutputDirectory) && Directory.EnumerateFiles(OutputDirectory, projectName + ".dll", SearchOption.AllDirectories).Any();
            }
        }
    }
}