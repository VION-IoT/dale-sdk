using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Sdk.Generators.Test.Helpers
{
    /// <summary>
    ///     The harness for a rule whose subject is the boundary between a compilation and its references. The
    ///     ordinary harness injects the stub attribute file as a source document; this one emits it, and any
    ///     other library, as a real referenced assembly, which is what the SDK is in every consumer's build.
    /// </summary>
    public static class MetadataCompilation
    {
        /// <summary>The stub attribute file emitted as the referenced assembly <c>DaleSdkStub</c>.</summary>
        public static async Task<MetadataReference> CompileSdkStubAsync()
        {
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(MetadataCompilation).Assembly.Location)!,
                                         "..",
                                         "..",
                                         "..",
                                         "Helpers",
                                         "TestAttributeStubs.cs");
            return await CompileLibraryAsync("DaleSdkStub", File.ReadAllText(stubsPath));
        }

        /// <summary>One source file emitted as a referenced assembly of the given name.</summary>
        public static async Task<MetadataReference> CompileLibraryAsync(string assemblyName, string source, params MetadataReference[] additionalReferences)
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

        /// <summary>
        ///     Compiles <paramref name="consumerSource" /> as the assembly <c>Consumer</c> against the references, requires
        ///     it to compile, and returns what <paramref name="analyzer" /> reports over it.
        /// </summary>
        public static async Task<Diagnostic[]> RunAnalyzerAsync(string consumerSource, DiagnosticAnalyzer analyzer, params MetadataReference[] references)
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