using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

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

            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync();
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
            return System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assembly.Location)!,
                                                                     "..",
                                                                     "..",
                                                                     "..",
                                                                     "Helpers",
                                                                     "TestAttributeStubs.cs"));
        }
    }
}