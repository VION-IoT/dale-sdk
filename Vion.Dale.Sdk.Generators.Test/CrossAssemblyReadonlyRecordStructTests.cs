using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     Cross-assembly metadata-path coverage for DALE003 / DALE016.
    ///     The existing tests compile a single source string — they never exercise the
    ///     <see cref="INamedTypeSymbol" /> instances Roslyn produces from PE metadata,
    ///     which is where the symbol-property values diverge from compilations-from-source.
    /// </summary>
    [TestClass]
    public class CrossAssemblyReadonlyRecordStructTests
    {
        /// <summary>
        ///     Documents the Roslyn metadata-reader quirk that motivates the
        ///     <c>HasRecordStructMarker</c> fallback in <c>AnalyzerHelper</c>: when a record
        ///     struct is loaded from a referenced assembly, <see cref="INamedTypeSymbol.IsRecord" />
        ///     returns <c>false</c> (Roslyn detects records via the <c>&lt;Clone&gt;$</c> method,
        ///     which record structs never had — see dotnet/roslyn#63566). The synthesized
        ///     <c>Deconstruct</c> method stays visible via <c>GetMembers</c> and serves as
        ///     a reliable structural marker — emitted for every positional record and never for
        ///     plain structs or system value types like <c>decimal</c>. If this assertion ever
        ///     flips, the analyzer fallback becomes a no-op safety net (still safe to keep) but
        ///     the comment can be revisited.
        /// </summary>
        [TestMethod]
        public async Task RoslynMetadataReader_RecordStruct_IsRecordFalse_HasDeconstructMarker()
        {
            var libRef = await CompileLibraryAsync(@"
namespace Lib
{
    public readonly record struct Coords(double Lat, double Lon);
}
");

            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var consumerCompilation = CSharpCompilation.Create("Consumer",
                                                               new[]
                                                               {
                                                                   CSharpSyntaxTree.ParseText(@"
using Lib;
public class C { public Coords P { get; set; } }
"),
                                                               },
                                                               refs.Add(libRef),
                                                               new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compileErrors = consumerCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.IsEmpty(compileErrors, "consumer compilation has errors: " + string.Join("; ", compileErrors.Select(d => d.ToString())));

            var coords = consumerCompilation.GetTypeByMetadataName("Lib.Coords");
            Assert.IsNotNull(coords, "Lib.Coords not found in consumer compilation");

            Assert.IsTrue(coords!.IsValueType, "Coords should be a value type");
            Assert.IsTrue(coords.IsReadOnly, "Coords should report IsReadOnly across assemblies");
            Assert.IsFalse(coords.IsRecord,
                           "Sanity check — Roslyn currently reports IsRecord=false for record structs " +
                           "from metadata. If this becomes true, the Deconstruct fallback in AnalyzerHelper " +
                           "is redundant; the analyzer still works either way.");
            Assert.IsTrue(coords.GetMembers("Deconstruct").OfType<IMethodSymbol>().Any(),
                          "Positional record structs emit Deconstruct; this is the structural marker the analyzer falls back to.");
        }

        /// <summary>
        ///     DALE003 / DALE016 regression: a flat readonly record struct defined in a
        ///     referenced assembly must be accepted as a [ServiceProperty] type. Mirrors
        ///     <c>StructServiceElementAnalyzerTests.ValidFlatReadonlyRecordStruct_NoDiagnostic</c>
        ///     but with the struct living in metadata, not source.
        /// </summary>
        [TestMethod]
        public async Task ValidFlatReadonlyRecordStruct_FromReferencedAssembly_StructAnalyzer_NoDiagnostic()
        {
            var libRef = await CompileLibraryAsync(@"
namespace Lib
{
    public readonly record struct Coords(double Lat, double Lon);
}
");

            var consumerSource = @"
using Lib;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public Coords Position { get; set; }
}";

            await VerifyAnalyzerWithReferenceAsync<StructServiceElementAnalyzer>(consumerSource, libRef);
        }

        /// <summary>
        ///     DALE003 mirror of the above — the type-support analyzer must also accept
        ///     the cross-assembly struct.
        /// </summary>
        [TestMethod]
        public async Task ValidFlatReadonlyRecordStruct_FromReferencedAssembly_TypeAnalyzer_NoDiagnostic()
        {
            var libRef = await CompileLibraryAsync(@"
namespace Lib
{
    public readonly record struct Coords(double Lat, double Lon);
}
");

            var consumerSource = @"
using Lib;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public Coords Position { get; set; }
}";

            await VerifyAnalyzerWithReferenceAsync<ServiceElementTypeAnalyzer>(consumerSource, libRef);
        }

        /// <summary>
        ///     Compiles the given source into an in-memory PE image and returns it as a
        ///     <see cref="PortableExecutableReference" />. Used to construct the metadata
        ///     path the analyzer normally only sees in real consumer projects.
        /// </summary>
        private static async Task<MetadataReference> CompileLibraryAsync(string source)
        {
            var refs = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var compilation = CSharpCompilation.Create("Lib",
                                                       new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                                                       refs,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                var errors = string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                Assert.Fail("Lib compilation failed:\n" + errors);
            }

            stream.Position = 0;
            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        /// <summary>
        ///     Runs an analyzer test with the standard Dale attribute stubs plus an extra
        ///     metadata reference, expecting no diagnostics.
        /// </summary>
        private static async Task VerifyAnalyzerWithReferenceAsync<TAnalyzer>(string source, MetadataReference additionalReference)
            where TAnalyzer : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer, new()
        {
            var stubsPath = Path.Combine(Path.GetDirectoryName(typeof(CrossAssemblyReadonlyRecordStructTests).Assembly.Location)!,
                                         "..",
                                         "..",
                                         "..",
                                         "Helpers",
                                         "TestAttributeStubs.cs");
            var stubs = File.ReadAllText(stubsPath);

            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
                       {
                           TestCode = source,
                           ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                       };
            test.TestState.Sources.Add(("TestAttributeStubs.cs", stubs));
            test.TestState.AdditionalReferences.Add(additionalReference);

            await test.RunAsync();
        }
    }
}
