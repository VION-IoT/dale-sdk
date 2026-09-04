using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Sdk.Generators.Test.Helpers
{
    /// <summary>
    ///     Base utilities for analyzer tests. Provides verifier methods that include
    ///     the test attribute stubs so that test source code referencing Dale attributes compiles.
    /// </summary>
    public static class AnalyzerTestBase
    {
        /// <summary>
        ///     The attribute stub source that is added to every test compilation.
        /// </summary>
        private static readonly string AttributeStubs = GetAttributeStubs();

        /// <summary>
        ///     Verifies that the analyzer produces the expected diagnostics on the given source.
        /// </summary>
        public static async Task VerifyAnalyzerAsync<TAnalyzer>(string source, params DiagnosticResult[] expected)
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
                       {
                           TestCode = source,
                           ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                       };

            // Add attribute stubs as an additional source file
            test.TestState.Sources.Add(("TestAttributeStubs.cs", AttributeStubs));

            // Pin LanguageVersion so tests can exercise newer C# features (e.g. C# 13 'field'
            // contextual keyword) regardless of what the test framework's default would pick.
            test.SolutionTransforms.Add((solution, projectId) =>
                                        {
                                            var project = solution.GetProject(projectId)!;
                                            var parseOptions = (CSharpParseOptions)project.ParseOptions!;
                                            return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(LanguageVersion.Latest));
                                        });

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
        }

        /// <summary>
        ///     Runs several analyzers over one compilation and returns everything they reported, in id
        ///     order. The verifier above runs exactly one analyzer, so a rule that suppressed another
        ///     reads there the same as one that did not — this is the harness for a claim about two
        ///     diagnostics meeting on one declaration.
        /// </summary>
        public static async Task<Diagnostic[]> RunAnalyzersAsync(string source, params DiagnosticAnalyzer[] analyzers)
        {
            var references = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, default);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var compilation = CSharpCompilation.Create("MultiAnalyzer",
                                                       new[]
                                                       {
                                                           CSharpSyntaxTree.ParseText(AttributeStubs, parseOptions),
                                                           CSharpSyntaxTree.ParseText(source, parseOptions),
                                                       },
                                                       references,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.IsEmpty(compileErrors, "test compilation has errors: " + string.Join("; ", compileErrors.Select(d => d.ToString())));

            var reported = await compilation.WithAnalyzers(ImmutableArray.Create(analyzers)).GetAnalyzerDiagnosticsAsync(default);
            return reported.OrderBy(d => d.Id, System.StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        ///     Creates a DiagnosticResult for the given descriptor.
        /// </summary>
        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
        {
            return new DiagnosticResult(descriptor);
        }

        private static string GetAttributeStubs()
        {
            var assembly = typeof(AnalyzerTestBase).Assembly;
            return File.ReadAllText(Path.Combine(Path.GetDirectoryName(assembly.Location)!,
                                                 "..",
                                                 "..",
                                                 "..",
                                                 "Helpers",
                                                 "TestAttributeStubs.cs"));
        }
    }
}