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
    ///     DALE034 cross-assembly coverage: the foundation-library pattern where the
    ///     <c>IChangeThreshold&lt;T&gt;</c> lives in a shared referenced assembly (e.g. Common.Lib)
    ///     while the <c>MinChange</c> knob is declared in another, referencing assembly. The analyzer
    ///     must look across referenced assemblies — mirroring the runtime scan — so a layered design
    ///     compiles cleanly instead of erroring on a threshold it cannot see.
    /// </summary>
    [TestClass]
    public class MinChangeCrossAssemblyThresholdAnalyzerTests
    {
        [TestMethod]
        public async Task MinChangeWithThresholdInReferencedAssembly_NoDiagnostic()
        {
            var sdkRef = await CompileSdkStubAsync();
            var commonRef = await CompileLibraryAsync("Common",
                                                      @"
using Vion.Dale.Sdk.Emission;

namespace Common
{
    public readonly record struct Coordinate(double Lat, double Lon);

    public sealed class CoordinateChangeThreshold : IChangeThreshold<Coordinate>
    {
        public bool Exceeds(in Coordinate last, in Coordinate now, string threshold) => false;
    }
}
",
                                                      sdkRef);

            var consumerSource = @"
using Common;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.0001"")] public Coordinate Location { get; set; }
}";

            var dale034 = await RunMinChangeAnalyzerAsync(consumerSource, sdkRef, commonRef);

            Assert.IsEmpty(dale034,
                           "DALE034 must NOT fire when IChangeThreshold<Coordinate> lives in a referenced assembly: " + string.Join("; ", dale034.Select(d => d.GetMessage())));
        }

        [TestMethod]
        public async Task MinChangeWithoutThresholdAnywhere_ReportsDiagnostic()
        {
            // Negative control: same layout, but the referenced library declares no threshold for the type.
            var sdkRef = await CompileSdkStubAsync();
            var commonRef = await CompileLibraryAsync("Common",
                                                      @"
namespace Common
{
    public readonly record struct Coordinate(double Lat, double Lon);
}
",
                                                      sdkRef);

            var consumerSource = @"
using Common;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinChange = ""0.0001"")] public Coordinate Location { get; set; }
}";

            var dale034 = await RunMinChangeAnalyzerAsync(consumerSource, sdkRef, commonRef);

            Assert.IsTrue(dale034.Any(), "DALE034 must fire when no IChangeThreshold<Coordinate> exists in any assembly.");
        }

        private static async Task<MetadataReference> CompileSdkStubAsync()
        {
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(MinChangeCrossAssemblyThresholdAnalyzerTests).Assembly.Location)!,
                                         "..",
                                         "..",
                                         "..",
                                         "Helpers",
                                         "TestAttributeStubs.cs");
            return await CompileLibraryAsync("DaleSdkStub", File.ReadAllText(stubsPath));
        }

        private static async Task<MetadataReference> CompileLibraryAsync(string assemblyName, string source, params MetadataReference[] additionalReferences)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var compilation = CSharpCompilation.Create(assemblyName,
                                                       new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                                                       refs.AddRange(additionalReferences),
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                var errors = string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                Assert.Fail($"{assemblyName} compilation failed:\n{errors}");
            }

            stream.Position = 0;
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static async Task<Diagnostic[]> RunMinChangeAnalyzerAsync(string consumerSource, params MetadataReference[] references)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var compilation = CSharpCompilation.Create("Consumer",
                                                       new[] { CSharpSyntaxTree.ParseText(consumerSource, parseOptions) },
                                                       refs.AddRange(references),
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.IsEmpty(compileErrors, "consumer compilation has errors: " + string.Join("; ", compileErrors.Select(d => d.ToString())));

            var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MinChangeWithoutChangeThresholdAnalyzer()));
            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(default);
            return diagnostics.Where(d => d.Id == "DALE034").ToArray();
        }
    }
}