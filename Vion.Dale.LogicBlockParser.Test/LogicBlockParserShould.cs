using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Dale.Plugin.Test.TestHelpers;

namespace Vion.Dale.LogicBlockParser.Test
{
    /// <summary>
    ///     The pack-time producer, exercised the way <c>dotnet pack</c> exercises it
    ///     (<c>docs/specs/introspection.md</c>): a real logic-block assembly on disk, read through the real
    ///     command line, asserted against the file that comes out.
    ///     <para>
    ///         It shells out to the built <c>Vion.Dale.LogicBlockParser.dll</c> rather than calling
    ///         <c>Main</c>, because the exit code and the presence of the output file are half of what is
    ///         under test, and because the parser loads its plugin into an <c>AssemblyLoadContext</c> whose
    ///         shared-extension registry is process-wide static state — a second in-process run would see
    ///         the first one's.
    ///     </para>
    ///     <para>
    ///         The two fixture plugins are <c>ProjectReference</c>d, so both land in this project's output
    ///         directory beside the whole SDK closure. That directory is what the parser is pointed at.
    ///     </para>
    ///     <para>
    ///         The Dale SDK version annotations must not depend on the version the build stamps on the
    ///         SDK: <c>0.0.0</c> at a desk or pull-request build, the release's at a tag build. Those tests
    ///         compile a plugin of their own into a directory of its own, against a stand-in SDK at
    ///         <c>0.0.0</c>, beside an assembly built against a stand-in SDK at a chosen version within
    ///         the build's major — another major meets the host's major-version gate once that is known.
    ///     </para>
    /// </summary>
    [TestClass]
    public class LogicBlockParserShould
    {
        private const string PluginAssembly = "Vion.Dale.ParserProbe.dll";

        private const string UnregisteredPluginAssembly = "Vion.Dale.ParserProbe.Unregistered.dll";

        /// <summary>Where the fixture plugins and the whole SDK closure sit: what the parser is pointed at.</summary>
        private static readonly string PluginDirectory = AppContext.BaseDirectory;

        /// <summary>
        ///     The major of the <c>Vion.Dale.Sdk</c> this build compiled, which the parser hosts: <c>0</c> at
        ///     <c>0.0.0</c>, the release's major at a tag build.
        /// </summary>
        private static readonly int BuildSdkMajor = AssemblyName.GetAssemblyName(Path.Combine(PluginDirectory, "Vion.Dale.Sdk.dll")).Version!.Major;

        /// <summary>
        ///     Where the documents these tests produce go. One directory per run, deleted after it: writing
        ///     them beside the plugins left a JSON file per test in the build output, and a build output that
        ///     grows every run is a mess a later `clean` fixes by accident.
        /// </summary>
        private static string _outputDirectory = null!;

        [ClassInitialize]
        public static void CreateOutputDirectory(TestContext context)
        {
            _outputDirectory = Directory.CreateTempSubdirectory("dale-parser-test-").FullName;
        }

        [ClassCleanup]
        public static void RemoveOutputDirectory()
        {
            Directory.Delete(_outputDirectory, true);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.1")]
        public void EmitOneRecordPerLogicBlock()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParser(PluginAssembly, output);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Output);
            var document = ReadDocument(output);
            CollectionAssert.AreEqual(new[] { "Vion.Dale.ParserProbe.DevelopmentOnlyBlock", "Vion.Dale.ParserProbe.Grouping+NestedBlock", "Vion.Dale.ParserProbe.PlainBlock" },
                                      BlockNames(document));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.5")]
        public void OrderLogicBlocksByFullTypeName()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            var names = BlockNames(ReadDocument(output));
            CollectionAssert.AreEqual(names.OrderBy(name => name, StringComparer.Ordinal).ToList(), names);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.1")]
        public void ReportNestedBlockIdentityWithItsClrNestingSeparator()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            Assert.Contains("Vion.Dale.ParserProbe.Grouping+NestedBlock", BlockNames(ReadDocument(output)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.2")]
        public void ReportSuppliedPackageIdentity()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParser(PluginAssembly, output, "--package-id", "Contoso.Supplied");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Output);
            Assert.AreEqual("Contoso.Supplied", ReadDocument(output)["packageId"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.2")]
        public void FallBackToAssemblyNameWhenNoPackageIdentitySupplied()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            Assert.AreEqual("Vion.Dale.ParserProbe", ReadDocument(output)["packageId"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.2")]
        public void FallBackToAssemblyNameWhenSuppliedPackageIdentityBlank()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output, "--package-id", "   ");

            // Assert
            Assert.AreEqual("Vion.Dale.ParserProbe", ReadDocument(output)["packageId"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.3")]
        public void ReportPluginAssemblyVersion()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            Assert.AreEqual("7.8.9-probe.1", ReadDocument(output)["packageVersion"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.4")]
        public void ReportEmptyDocumentLevelAnnotations()
        {
            // Arrange — the plugin's only reference to the SDK is to the 0.0.0 stand-in, which is unknown
            var plugin = EmitPlugin(null, null);
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            Assert.IsEmpty((JsonObject)ReadDocument(output)["annotations"]!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.4")]
        public void ReportOnlyDaleSdkVersionAnnotations()
        {
            // Arrange
            var plugin = EmitPlugin(AtBuildMajor(14, 3).ToString(3), AtBuildMajor(15, 1));
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            var keys = ((JsonObject)ReadDocument(output)["annotations"]!).Select(annotation => annotation.Key).Order(StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(new[] { "minimumDaleSdkVersion", "targetDaleSdkVersion" }, keys);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.7")]
        [DataRow(15, 1)]
        [DataRow(3, 0)]
        public void ReportTargetDaleSdkVersion(int minor, int patch)
        {
            // Arrange
            var target = AtBuildMajor(minor, patch);
            var plugin = EmitPlugin(null, target);
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            Assert.AreEqual(target.ToString(3), ReadDocument(output)["annotations"]?["targetDaleSdkVersion"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.7")]
        public void OmitTargetDaleSdkVersionWhenUnknown()
        {
            // Arrange — the plugin declares a minimum, so the map is not empty for want of anything to say;
            // its only reference to the SDK is to the 0.0.0 stand-in, which is unknown
            var plugin = EmitPlugin(AtBuildMajor(14, 3).ToString(3), null);
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            var annotations = (JsonObject)ReadDocument(output)["annotations"]!;
            Assert.IsTrue(annotations.ContainsKey("minimumDaleSdkVersion"), "The fixture must carry an annotation for the absence beside it to mean anything.");
            Assert.IsFalse(annotations.ContainsKey("targetDaleSdkVersion"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.8")]
        [DataRow(14, 3, DisplayName = "below the target")]
        [DataRow(16, 0, DisplayName = "above the target")]
        public void ReportDeclaredMinimumDaleSdkVersion(int minor, int patch)
        {
            // Arrange
            var declared = AtBuildMajor(minor, patch).ToString(3);
            var plugin = EmitPlugin(declared, AtBuildMajor(15, 1));
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            Assert.AreEqual(declared, ReadDocument(output)["annotations"]?["minimumDaleSdkVersion"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.8")]
        public void OmitMinimumDaleSdkVersionWhenNoneDeclared()
        {
            // Arrange — the target is known, so the map is not empty for want of anything to say
            var plugin = EmitPlugin(null, AtBuildMajor(15, 1));
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParserRaw(plugin, output);

            // Assert
            var annotations = (JsonObject)ReadDocument(output)["annotations"]!;
            Assert.IsTrue(annotations.ContainsKey("targetDaleSdkVersion"), "The fixture must carry an annotation for the absence beside it to mean anything.");
            Assert.IsFalse(annotations.ContainsKey("minimumDaleSdkVersion"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.6")]
        public void WriteCamelCasedMemberNamesAndAnnotationKeys()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            var plain = Block(ReadDocument(output), "Vion.Dale.ParserProbe.PlainBlock");
            Assert.IsNotNull(plain["typeFullName"]);
            Assert.AreEqual("Plain", plain["annotations"]?["defaultName"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-003.1")]
        public void EmitByteIdenticalDocumentsForRepeatedRuns()
        {
            // Arrange
            var first = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");
            var second = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, first);
            RunParser(PluginAssembly, second);

            // Assert
            CollectionAssert.AreEqual(Hash(first), Hash(second), "Two runs over one assembly produced different documents.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.6")]
        [TestProperty("spec", "AC-INTRO-002.7")]
        public void ExcludeDevelopmentOnlyBlocksAndNameThem()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParser(PluginAssembly, output, "--exclude-development-only");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Output);
            CollectionAssert.DoesNotContain(BlockNames(ReadDocument(output)), "Vion.Dale.ParserProbe.DevelopmentOnlyBlock");
            Assert.Contains("Vion Dale: ", run.Output);
            Assert.Contains("Vion.Dale.ParserProbe.DevelopmentOnlyBlock", run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.5")]
        public void AcceptOptionsInAnyPositionAndCase()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParserRaw("--EXCLUDE-DEVELOPMENT-ONLY", Path.Combine(PluginDirectory, PluginAssembly), output);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Output);
            CollectionAssert.DoesNotContain(BlockNames(ReadDocument(output)), "Vion.Dale.ParserProbe.DevelopmentOnlyBlock");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.1")]
        public void RefusePluginWhoseConcreteBlockUnregistered()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParser(UnregisteredPluginAssembly, output);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Output);
            Assert.Contains("Vion.Dale.ParserProbe.Unregistered.ForgottenBlock", run.Output);
            Assert.IsFalse(File.Exists(output), "A refused run must leave no document behind.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.3")]
        [DataRow(0, DisplayName = "no arguments")]
        [DataRow(1, DisplayName = "plugin path only")]
        public void RefuseIncompleteCommandLine(int argumentCount)
        {
            // Arrange
            var arguments = new[] { Path.Combine(PluginDirectory, PluginAssembly) }.Take(argumentCount).ToArray();

            // Act
            var run = RunParserRaw(arguments);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.Contains("Usage:", run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.4")]
        public void RefuseMissingPluginPath()
        {
            // Arrange
            var missing = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.dll");

            // Act
            var run = RunParserRaw(missing, Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json"));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.Contains(missing, run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.9")]
        public void RefuseUnregisteredBlockEvenWhenExclusionWouldDropIt()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            // The forgotten block binds a provider face, so the filter would leave it out of the document.
            var run = RunParser(UnregisteredPluginAssembly, output, "--exclude-development-only");

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Output);
            Assert.Contains("Vion.Dale.ParserProbe.Unregistered.ForgottenBlock", run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.10")]
        [DataRow("0.14", DisplayName = "two parts")]
        [DataRow("0.14.3-rc.1", DisplayName = "prerelease")]
        public void RefuseMalformedDeclaredMinimum(string declared)
        {
            // Arrange
            var target = AtBuildMajor(15, 1);
            var plugin = EmitPlugin(declared, target);
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParserRaw(plugin, output);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Output);
            Assert.IsFalse(File.Exists(output), "A refused run writes no document.");
            Assert.Contains($"'{declared}'", run.Output);
            Assert.Contains(target.ToString(3), run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.10")]
        public void RefuseDeclaredMinimumOfAnotherMajor()
        {
            // Arrange
            var declared = $"{BuildSdkMajor + 1}.0.0";
            var target = AtBuildMajor(15, 1);
            var plugin = EmitPlugin(declared, target);
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParserRaw(plugin, output);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Output);
            Assert.IsFalse(File.Exists(output), "A refused run writes no document.");
            Assert.Contains(declared, run.Output);
            Assert.Contains(target.ToString(3), run.Output);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.5")]
        public void IntrospectNoAbstractLogicBlock()
        {
            // Arrange
            var output = Path.Combine(_outputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            // The unregistered plugin carries an abstract block that the registration also omits. Only the
            // concrete one may be refused; an abstract type is never introspected, so it is never missing.
            var run = RunParser(UnregisteredPluginAssembly, output);

            // Assert
            // The exit code and the named concrete block are what place the run past the registration check:
            // a run that died loading the plugin names no block at all and would satisfy the absence alone.
            Assert.AreEqual(1, run.ExitCode, run.Output);
            Assert.Contains("Vion.Dale.ParserProbe.Unregistered.ForgottenBlock", run.Output);
            Assert.DoesNotContain("AbstractBaseBlock", run.Output);
        }

        /// <summary>A version within the major of the SDK this build compiled, which the parser's major-version gate admits.</summary>
        private static Version AtBuildMajor(int minor, int patch)
        {
            return new Version(BuildSdkMajor, minor, patch, 0);
        }

        /// <summary>
        ///     Compiles a plugin with no logic block into a directory of its own and returns its path. It
        ///     declares <paramref name="declaredMinimum" /> as its minimum Dale SDK, and a second assembly
        ///     beside it is built against a stand-in <c>Vion.Dale.Sdk</c> at <paramref name="dependencySdk" />.
        ///     The plugin itself is built against a stand-in SDK at <c>0.0.0</c>, never the real one, whose
        ///     version is whatever the build stamped: the dependency alone decides the target. The directory
        ///     holds nothing else: the parser's load context resolves the SDK and everything it references
        ///     from the parser's own process, by name.
        /// </summary>
        private static string EmitPlugin(string? declaredMinimum, Version? dependencySdk)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_outputDirectory, $"plugin-{Guid.NewGuid():N}")).FullName;
            var dependencyInjection = Path.Combine(PluginDirectory, "Microsoft.Extensions.DependencyInjection.Abstractions.dll");
            var unknownSdk = FixtureAssembly.Emit(Directory.CreateDirectory(Path.Combine(directory, "unknown-sdk")).FullName,
                                                  "Vion.Dale.Sdk",
                                                  new[] { dependencyInjection },
                                                  new[] { """[assembly: System.Reflection.AssemblyVersion("0.0.0.0")]""" },
                                                  """
                                                  namespace Vion.Dale.Sdk.Core
                                                  {
                                                      public interface IConfigureServices
                                                      {
                                                          void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection);
                                                      }
                                                  }
                                                  """);

            if (dependencySdk != null)
            {
                var standInSdk = FixtureAssembly.EmitStandInSdk(Directory.CreateDirectory(Path.Combine(directory, "stand-in")).FullName, dependencySdk);
                FixtureAssembly.Emit(directory,
                                     FixtureAssembly.UniqueName("Dependency"),
                                     new[] { standInSdk },
                                     body: $"public class FixtureMarker : {FixtureAssembly.StandInSdkAnchorType} {{ }}");
            }

            return FixtureAssembly.Emit(directory,
                                        FixtureAssembly.UniqueName("Vion.Dale.ParserProbe.Emitted"),
                                        new[] { unknownSdk, dependencyInjection },
                                        declaredMinimum == null ? null :
                                            new[] { $"[assembly: System.Reflection.AssemblyMetadata(\"MinimumDaleSdkVersion\", \"{declaredMinimum}\")]" },
                                        """
                                        public sealed class Registration : Vion.Dale.Sdk.Core.IConfigureServices
                                        {
                                            public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection)
                                            {
                                            }
                                        }
                                        """);
        }

        private static JsonObject ReadDocument(string path)
        {
            Assert.IsTrue(File.Exists(path), $"The parser wrote no document at {path}.");
            return (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        }

        private static JsonObject Block(JsonObject document, string typeFullName)
        {
            var block = ((JsonArray)document["logicBlocks"]!).OfType<JsonObject>().FirstOrDefault(b => b["typeFullName"]?.GetValue<string>() == typeFullName);
            Assert.IsNotNull(block, $"{typeFullName} is not in the document.");
            return block;
        }

        private static List<string> BlockNames(JsonObject document)
        {
            return ((JsonArray)document["logicBlocks"]!).Select(block => block!["typeFullName"]!.GetValue<string>()).ToList();
        }

        private static byte[] Hash(string path)
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }

        private static ParserRun RunParser(string pluginAssemblyFileName, string outputJsonPath, params string[] options)
        {
            return RunParserRaw(new[] { Path.Combine(PluginDirectory, pluginAssemblyFileName), outputJsonPath }.Concat(options).ToArray());
        }

        private static ParserRun RunParserRaw(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                WorkingDirectory = PluginDirectory,
                            };

            startInfo.ArgumentList.Add(ParserDll());
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new ParserRun(process.ExitCode, standardOutput.Result + standardError.Result);
        }

        /// <summary>
        ///     The parser's own build output. It is a <c>ProjectReference</c> of this project, so it is
        ///     always built; its directory is where its <c>runtimeconfig.json</c> lives, which is what
        ///     <c>dotnet &lt;dll&gt;</c> needs and what a project reference does not copy.
        /// </summary>
        private static string ParserDll()
        {
            var directory = new DirectoryInfo(PluginDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Vion.Dale.Sdk.sln")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root above the test output directory.");

            var configuration = PluginDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Release" :
                                    "Debug";
            var parser = Path.Combine(directory.FullName,
                                      "Vion.Dale.LogicBlockParser",
                                      "bin",
                                      configuration,
                                      "net10.0",
                                      "Vion.Dale.LogicBlockParser.dll");

            Assert.IsTrue(File.Exists(parser), $"The parser was not built at {parser}.");
            return parser;
        }

        private readonly record struct ParserRun(int ExitCode, string Output);
    }
}