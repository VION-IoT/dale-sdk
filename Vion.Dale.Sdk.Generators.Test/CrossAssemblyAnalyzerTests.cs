using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     The rules whose subject is the boundary between the compilation and its references. The
    ///     ordinary harness injects the stub attribute file as a source document, so nothing in it can
    ///     reproduce a consumer's compilation — where the SDK, and anything else, is metadata. These tests
    ///     emit a real referenced assembly instead.
    ///     <para>
    ///         <c>DALE026</c> is why the file exists: <c>Vion.Dale.Sdk.Core.PropertyGroup</c> is a source
    ///         declaration inside the SDK and metadata for every consumer, and a source-only lookup warned
    ///         about the group keys the platform itself ships.
    ///     </para>
    /// </summary>
    [TestClass]
    public class CrossAssemblyAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task StaySilentOnLiteralMatchingConstantInReferencedAssembly()
        {
            // Arrange
            var sdkReference = await CompileSdkStubAsync();

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""status"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics,
                           "DALE026 must not fire on a key the platform ships: every consumer reads PropertyGroup as metadata. " +
                           string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task StaySilentOnLiteralMatchingIntegratorConstantInReferencedAssembly()
        {
            // Arrange
            var sdkReference = await CompileSdkStubAsync();
            var conventions = await CompileLibraryAsync("Acme.Conventions",
                                                        @"
namespace Acme
{
    public static class PropertyGroup
    {
        public const string Powertrain = ""acme.powertrain"";
    }
}",
                                                        sdkReference);

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference, conventions);

            // Assert
            Assert.IsEmpty(diagnostics, "an integrator's own PropertyGroup class is a reference like any other: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task ReportLiteralMatchingNoConstantInAnyAssembly()
        {
            // Arrange — the negative control: the same layout, no matching constant anywhere.
            var sdkReference = await CompileSdkStubAsync();

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference);

            // Assert
            Assert.HasCount(1, diagnostics);
            Assert.Contains("acme.powertrain", diagnostics[0].GetMessage());
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.5")]
        public async Task ReportLiteralMatchingConstantInClassNotNamedPropertyGroup()
        {
            // Arrange — only a class named exactly PropertyGroup is read; a suffixed name is not.
            var sdkReference = await CompileSdkStubAsync();
            var conventions = await CompileLibraryAsync("Acme.Conventions",
                                                        @"
namespace Acme
{
    public static class EcoPropertyGroup
    {
        public const string Powertrain = ""acme.powertrain"";
    }
}",
                                                        sdkReference);

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference, conventions);

            // Assert
            Assert.HasCount(1, diagnostics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-002.3")]
        public async Task StaySilentOnPublicApiTypeDeclaredInReferencedAssembly()
        {
            // Arrange — an undocumented [PublicApi] type in a REFERENCED assembly, in a declared namespace.
            var sdkReference = await CompileSdkStubAsync();
            var library = await CompileLibraryAsync("Library",
                                                    @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""Api"")]

namespace Api
{
    [PublicApi] public class Undocumented { }
}",
                                                    sdkReference);

            var source = @"
public class Consumer { }";

            // Act
            var diagnostics = await RunAsync(source, new PublicApiDocumentationAnalyzer(), sdkReference, library);

            // Assert
            Assert.IsEmpty(diagnostics, "a referenced assembly's declarations are its own build's business: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        private static async Task<MetadataReference> CompileSdkStubAsync()
        {
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(CrossAssemblyAnalyzerTests).Assembly.Location)!, "..", "..", "..", "Helpers", "TestAttributeStubs.cs");
            return await CompileLibraryAsync("DaleSdkStub", File.ReadAllText(stubsPath));
        }

        private static async Task<MetadataReference> CompileLibraryAsync(string assemblyName, string source, params MetadataReference[] additionalReferences)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var compilation = CSharpCompilation.Create(assemblyName,
                                                       new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
                                                       refs.AddRange(additionalReferences),
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                Assert.Fail($"{assemblyName} compilation failed:\n" + string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }

            stream.Position = 0;
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static Task<Diagnostic[]> RunAsync(string consumerSource, params MetadataReference[] references)
        {
            return RunAsync(consumerSource, new LiteralGroupKeyAnalyzer(), references);
        }

        private static async Task<Diagnostic[]> RunAsync(string consumerSource, DiagnosticAnalyzer analyzer, params MetadataReference[] references)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var compilation = CSharpCompilation.Create("Consumer",
                                                       new[] { CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.Latest)) },
                                                       refs.AddRange(references),
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.IsEmpty(compileErrors, "consumer compilation has errors: " + string.Join("; ", compileErrors.Select(d => d.ToString())));

            var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(default);
            return diagnostics.ToArray();
        }
    }
}
