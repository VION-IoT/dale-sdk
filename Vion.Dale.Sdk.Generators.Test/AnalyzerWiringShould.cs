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
    ///         Every build runs once, before the first test: one MSBuild invocation per set of global properties,
    ///         building all of that set's projects. A separate <c>dotnet build</c> per project recompiled the
    ///         shared dependency graph each time, and those builds were most of this project's test time. A
    ///         diagnostic is judged by the project MSBuild attributes it to, the <c>[path.csproj]</c> suffix
    ///         every diagnostic line carries.
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
        ///     guard proves; nothing here fingerprints their outputs.
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
        ///     The two sets <see cref="LeaveBuildOutputsOfDependencyGraphUntouched" /> needs, stamped with a
        ///     version no other build uses. What triggered the clobber in CI was the child build's inputs
        ///     DIFFERING from the stamped build's: a same-inputs child build is up to date and rewrites nothing,
        ///     which is how an unisolated build looks innocent locally. Naming a version no other build uses
        ///     forces the recompile the guard has to survive.
        /// </summary>
        private static readonly BuildSet[] GuardSets =
        [
            new("GuardProbe", IoProbeProperty, WiringGuardVersion, ProbedProjects),
            new("GuardOrdinary", null, WiringGuardVersion, ProbedProjects),
        ];

        private static readonly Dictionary<string, BuildResult> Results = new(StringComparer.Ordinal);

        private static Dictionary<string, string> _fingerprintsBefore = null!;

        private static Dictionary<string, string> _fingerprintsAfter = null!;

        private static string _scratch = null!;

        [ClassInitialize]
        public static void BuildEverySet(TestContext context)
        {
            // Everything these builds produce — for the projects and for their whole ProjectReference graph —
            // lands under here and is thrown away after the last test.
            _scratch = Path.Combine(Path.GetTempPath(), "dale-analyzer-wiring", Guid.NewGuid().ToString("N"));

            // The fingerprints bracket every build of this class, not only the guard's own.
            _fingerprintsBefore = FingerprintProbeBuildGraph();

            foreach (var set in new[] { IoProbe, TestKitProbe, ModbusProbe, HttpProbe, Ordinary }.Concat(GuardSets))
            {
                Results[set.Name] = Build(set);
            }

            _fingerprintsAfter = FingerprintProbeBuildGraph();
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
            // Arrange / Act / Assert
            var build = Results[IoProbe.Name];

            Assert.AreNotEqual(0, build.ExitCode, $"The probe build of {projectName} succeeded, so the Dale analyzers did not run over it.\n{build.Output}");
            Assert.IsTrue(build.LinesOf(projectName).Any(line => line.Contains("error DALE046")),
                          $"The probe build of {projectName} failed for some other reason than DALE046.\n{build.Output}");
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
            var build = Results[HttpProbe.Name];

            // Act / Assert
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
            // Arrange / Act / Assert
            // The 0.11.1 regression, pinned. CI builds the solution stamped, runs the tests, then packs the
            // Release outputs it already built — so a test that shells an unstamped build of any project in
            // this graph replaces those outputs with 0.0.0.0 ones between the stamp and the pack.
            //
            // Compare bytes, not versions: locally nothing is stamped (Directory.Build.props falls back to
            // 0.0.0-local), so a clobbered assembly and an intact one carry the same 0.0.0.0 and a version
            // comparison would pass here while CI went on shipping the wrong bytes. That asymmetry is exactly
            // why the clobber stayed invisible until the packages were on nuget.org. Assembly fingerprints
            // carry the version as well, so a failure still names the stamp that was lost.
            // The probe half fails by design and emits nothing, so only the ordinary half can show the guard's
            // builds reached the compile at all.
            var guardOrdinary = Results[GuardSets[1].Name];
            Assert.IsTrue(ProbedProjects.All(guardOrdinary.Built),
                          $"The guard's ordinary build did not produce the probed projects, so there was nothing to guard.\n{guardOrdinary.Output}");

            var disturbed = _fingerprintsAfter.Where(entry => !_fingerprintsBefore.TryGetValue(entry.Key, out var fingerprint) || fingerprint != entry.Value)
                                              .Select(entry =>
                                                          $"{entry.Key}\n    before: {(_fingerprintsBefore.TryGetValue(entry.Key, out var was) ? was : "(did not exist)")}\n    after:  {entry.Value}")
                                              .Concat(_fingerprintsBefore.Keys.Where(path => !_fingerprintsAfter.ContainsKey(path)).Select(path => $"{path}\n    deleted"))
                                              .OrderBy(line => line, StringComparer.Ordinal)
                                              .ToList();

            Assert.IsEmpty(disturbed,
                           "The analyzer-wiring builds wrote into the repository's own build outputs. Those are what `dotnet pack` ships, " +
                           "and these builds carry no /p:Version — this is how 0.11.1 shipped lib assemblies stamped 0.0.0.0. Send the " +
                           $"child build somewhere disposable instead.\n{string.Join("\n", disturbed)}");
        }

        private static void AssertDale014InOwnNamespace(BuildResult build, string projectName, string declaredNamespace)
        {
            var lines = build.LinesOf(projectName).Where(line => line.Contains("DALE014")).ToList();

            Assert.IsNotEmpty(lines, $"The probe build of {projectName} drew no DALE014, so the Dale analyzers did not run over it.{Environment.NewLine}{build.Output}");
            Assert.IsTrue(lines.Any(line => line.Contains($"in namespace '{declaredNamespace}'")),
                          $"The DALE014 in the probe build of {projectName} named another namespace.{Environment.NewLine}{build.Output}");
        }

        /// <summary>
        ///     The ordinary build attributes no error and no <paramref name="diagnostic" /> to this project, and produced it —
        ///     the last so that a project that dropped out of the set, or whose lines stopped being attributed, cannot pass
        ///     by never having been built. Judged per project, so a probe leaking into one project fails that project's
        ///     test and not every other one sharing the build.
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
            File.WriteAllText(traversal,
                              "<Project><Target Name=\"Build\"><MSBuild Projects=\"" + string.Join(";", projectPaths.Values) +
                              "\" Targets=\"Build\" BuildInParallel=\"true\" /></Target></Project>");

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

        /// <summary>
        ///     An MSBuild directory property value: forward slashes and a trailing separator, which MSBuild
        ///     normalises on every OS. A trailing backslash would escape the closing quote of the argument
        ///     whenever the temp path contains a space.
        /// </summary>
        private static string MsBuildDirectory(string scratch, string leaf)
        {
            return Path.Combine(scratch, leaf).Replace('\\', '/') + '/';
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