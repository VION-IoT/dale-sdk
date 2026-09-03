using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// </summary>
    [TestClass]
    public class LogicBlockParserShould
    {
        private const string PluginAssembly = "Vion.Dale.ParserProbe.dll";

        private const string UnregisteredPluginAssembly = "Vion.Dale.ParserProbe.Unregistered.dll";

        private static readonly string OutputDirectory = AppContext.BaseDirectory;

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.1")]
        public void EmitOneRecordPerLogicBlock()
        {
            // Arrange
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            Assert.AreEqual("7.8.9-probe.1", ReadDocument(output)["packageVersion"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.4")]
        public void ReportEmptyDocumentLevelAnnotations()
        {
            // Arrange
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            RunParser(PluginAssembly, output);

            // Assert
            Assert.IsEmpty((JsonObject)ReadDocument(output)["annotations"]!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-001.6")]
        public void WriteCamelCasedMemberNamesAndAnnotationKeys()
        {
            // Arrange
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var first = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");
            var second = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

            // Act
            var run = RunParserRaw("--EXCLUDE-DEVELOPMENT-ONLY", Path.Combine(OutputDirectory, PluginAssembly), output);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Output);
            CollectionAssert.DoesNotContain(BlockNames(ReadDocument(output)), "Vion.Dale.ParserProbe.DevelopmentOnlyBlock");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.1")]
        public void RefusePluginWhoseConcreteBlockUnregistered()
        {
            // Arrange
            var output = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json");

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
            var arguments = new[] { Path.Combine(OutputDirectory, PluginAssembly) }.Take(argumentCount).ToArray();

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
            var missing = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.dll");

            // Act
            var run = RunParserRaw(missing, Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}.json"));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.Contains(missing, run.Output);
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
            return RunParserRaw(new[] { Path.Combine(OutputDirectory, pluginAssemblyFileName), outputJsonPath }.Concat(options).ToArray());
        }

        private static ParserRun RunParserRaw(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                WorkingDirectory = OutputDirectory,
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
            var directory = new DirectoryInfo(OutputDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Vion.Dale.Sdk.sln")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root above the test output directory.");

            var configuration = OutputDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
            var parser = Path.Combine(directory.FullName, "Vion.Dale.LogicBlockParser", "bin", configuration, "net10.0", "Vion.Dale.LogicBlockParser.dll");

            Assert.IsTrue(File.Exists(parser), $"The parser was not built at {parser}.");
            return parser;
        }

        private readonly record struct ParserRun(int ExitCode, string Output);
    }
}
