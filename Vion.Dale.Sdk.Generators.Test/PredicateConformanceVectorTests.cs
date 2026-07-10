using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Vion.Dale.Sdk.Generators.Predicates;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     Runs the vendored cross-implementation conformance vector against the analyzer's
    ///     recursive-descent parser. The analyzer <b>parses and type-checks but never evaluates</b>, so
    ///     only the vector's <c>parse</c> cases bind it directly; the <c>eval</c> cases are exercised as a
    ///     grammar cross-check (every evaluatable predicate must be inside the parse grammar). The
    ///     semantics of the <c>eval</c> cases are the JS/TS evaluators' and RFC 0016's future C# evaluator's
    ///     responsibility.
    ///     <para />
    ///     Source of truth: vion-contracts <c>Predicates/predicate-conformance.json</c> +
    ///     <c>docs/predicates.md</c>. See the provenance header in the vendored copy.
    /// </summary>
    [TestClass]
    public class PredicateConformanceVectorTests
    {
        [TestMethod]
        public void VendoredVector_HasCases()
        {
            var vector = LoadVector();
            Assert.IsNotEmpty(vector.Parse, "expected parse cases in the vendored vector");
            Assert.IsNotEmpty(vector.Eval, "expected eval cases in the vendored vector");
        }

        [TestMethod]
        public void ParseCases_MatchGrammar()
        {
            var vector = LoadVector();
            var failures = new List<string>();

            foreach (var testCase in vector.Parse)
            {
                var result = PredicateParser.Parse(testCase.Predicate);
                if (result.IsValid != testCase.Valid)
                {
                    failures.Add($"[{testCase.Name}] \"{testCase.Predicate}\": expected valid={testCase.Valid}, got valid={result.IsValid}" +
                                 (result.Error is null ? "" : $" (error: {result.Error})"));
                }
            }

            Assert.IsEmpty(failures, "parse-case mismatches:\n" + string.Join("\n", failures));
        }

        [TestMethod]
        public void EvalCases_AreAllInsideTheGrammar()
        {
            // The parser does not evaluate, but every eval predicate must at least parse — an eval case
            // outside the grammar would mean the vector and the dialect have drifted apart.
            var vector = LoadVector();
            var failures = new List<string>();

            foreach (var testCase in vector.Eval)
            {
                var result = PredicateParser.Parse(testCase.Predicate);
                if (!result.IsValid)
                {
                    failures.Add($"[{testCase.Name}] \"{testCase.Predicate}\": expected to parse, got error: {result.Error}");
                }
            }

            Assert.IsEmpty(failures, "eval-case predicates that fail to parse:\n" + string.Join("\n", failures));
        }

        private static ConformanceVector LoadVector()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(PredicateConformanceVectorTests).Assembly.Location)!, "Predicates", "predicate-conformance.json");
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
                          {
                              PropertyNameCaseInsensitive = true,
                              ReadCommentHandling = JsonCommentHandling.Skip,
                              AllowTrailingCommas = true,
                          };
            var vector = JsonSerializer.Deserialize<ConformanceVector>(json, options);
            Assert.IsNotNull(vector, "conformance vector failed to deserialize");
            return vector!;
        }

        // Positional records (constructor-bound), so the ReSharper cleanup can't strip an `init` accessor
        // and leave System.Text.Json unable to populate the fields. Case-insensitive matching (set on the
        // JsonSerializerOptions) maps the lower-case JSON keys onto these PascalCase parameters.
        private sealed record ConformanceVector(List<EvalCase> Eval, List<ParseCase> Parse);

        private sealed record EvalCase(string Name, string Predicate);

        private sealed record ParseCase(string Name, string Predicate, bool Valid);
    }
}