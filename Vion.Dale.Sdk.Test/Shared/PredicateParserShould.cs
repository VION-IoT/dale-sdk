using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Generators.Predicates;
using RuntimePredicate = Vion.Contracts.Predicates.Predicate;

namespace Vion.Dale.Sdk.Test.Shared
{
    /// <summary>
    ///     The linked analyzer parser (<c>Vion.Dale.Sdk.Generators/Predicates/PredicateParser.cs</c>, compiled
    ///     into this assembly) against <c>Vion.Contracts</c>' — the parser the runtime evaluates with.
    ///     <c>InclusionGate.EnsureResolvable</c> asks Contracts whether a gate's syntax is legal and the linked
    ///     parser for the tree whose references it checks, and refuses a gate outright where the two disagree.
    ///     That split is only sound while they implement one dialect, so every vector of the vendored
    ///     <c>predicate-conformance.json</c> goes through both here.
    ///     <para />
    ///     The vector is the canonical cross-implementation one from vion-contracts, linked from
    ///     <c>Vion.Dale.Sdk.Generators.Test/Predicates/</c> so there is one copy in the repo. The analyzer's own
    ///     <c>PredicateConformanceVectorTests</c> runs it through the linked parser alone; this class is the
    ///     half that was missing.
    /// </summary>
    [TestClass]
    public sealed class PredicateParserShould
    {
        private static readonly ConformanceVector Vector = LoadVector();

        [TestMethod]
        public void AgreeWithRuntimeParserOnEveryParseVector()
        {
            // Arrange
            var disagreements = new List<string>();

            // Act
            foreach (var vectorCase in Vector.Parse)
            {
                var linked = PredicateParser.Parse(vectorCase.Predicate).IsValid;
                var runtime = RuntimePredicate.TryParse(vectorCase.Predicate, out _, out var runtimeError);
                if (linked != runtime)
                {
                    disagreements.Add($"[{vectorCase.Name}] \"{vectorCase.Predicate}\": linked={linked}, runtime={runtime} ({runtimeError})");
                }
            }

            // Assert
            Assert.IsEmpty(disagreements, "the two parsers disagree on:\n" + string.Join("\n", disagreements));
            Assert.IsNotEmpty(Vector.Parse);
        }

        [TestMethod]
        public void AgreeWithRuntimeParserOnEveryEvalVector()
        {
            // Every eval predicate is inside the grammar by construction, so both parsers must accept all of
            // them — including the strict fail-closed cases, which fail at evaluation and not at parse.

            // Arrange
            var disagreements = new List<string>();

            // Act
            foreach (var vectorCase in Vector.Eval)
            {
                var linked = PredicateParser.Parse(vectorCase.Predicate).IsValid;
                var runtime = RuntimePredicate.TryParse(vectorCase.Predicate, out _, out var runtimeError);
                if (!linked || !runtime)
                {
                    disagreements.Add($"[{vectorCase.Name}] \"{vectorCase.Predicate}\": linked={linked}, runtime={runtime} ({runtimeError})");
                }
            }

            // Assert
            Assert.IsEmpty(disagreements, "an eval predicate one parser refuses:\n" + string.Join("\n", disagreements));
            Assert.IsNotEmpty(Vector.Eval);
        }

        [TestMethod]
        public void ExtractEveryNameEachEvalVectorResolves()
        {
            // What EnsureResolvable actually relies on: the walk over the linked tree finds exactly the names
            // the runtime evaluator will look up, which the vector states as the case's context keys. The one
            // exception is the fail-closed case whose context is deliberately empty — there the name is what
            // the evaluator cannot find, so the walk must still see it.

            // Arrange
            var mismatches = new List<string>();

            // Act
            foreach (var vectorCase in Vector.Eval)
            {
                var referenced = InclusionGate.ReferencedNames(PredicateParser.Parse(vectorCase.Predicate).Ast!).ToHashSet();
                var contextKeys = (vectorCase.Values ?? new Dictionary<string, JsonElement>()).Keys.ToHashSet();

                var satisfied = vectorCase.Error && contextKeys.Count == 0 ? referenced.Count > 0 : referenced.SetEquals(contextKeys);
                if (!satisfied)
                {
                    mismatches.Add($"[{vectorCase.Name}] \"{vectorCase.Predicate}\": walked [{string.Join(", ", referenced.Order())}], context [{string.Join(", ", contextKeys.Order())}]");
                }
            }

            // Assert
            Assert.IsEmpty(mismatches, "the walk and the vector's context disagree on:\n" + string.Join("\n", mismatches));
        }

        private static ConformanceVector LoadVector()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(PredicateParserShould).Assembly.Location)!, "Predicates", "predicate-conformance.json");
            var options = new JsonSerializerOptions
                          {
                              PropertyNameCaseInsensitive = true,
                              ReadCommentHandling = JsonCommentHandling.Skip,
                              AllowTrailingCommas = true,
                          };

            return JsonSerializer.Deserialize<ConformanceVector>(File.ReadAllText(path), options)!;
        }

        // Positional records, mirroring the analyzer project's harness: constructor-bound, so the ReSharper
        // cleanup cannot strip an `init` accessor and leave System.Text.Json unable to populate the fields.
        private sealed record ConformanceVector(List<EvalCase> Eval, List<ParseCase> Parse);

        private sealed record EvalCase(string Name, string Predicate, Dictionary<string, JsonElement>? Values = null, bool Error = false);

        private sealed record ParseCase(string Name, string Predicate, bool Valid);
    }
}