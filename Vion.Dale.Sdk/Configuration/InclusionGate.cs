using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Configuration
{
    /// <summary>
    ///     Shared resolution of <see cref="IncludedWhenAttribute" /> gates for the three declarative
    ///     binders (RFC 0016). One authority so interface, contract, and service binders resolve the
    ///     same gate identically for a given instance.
    /// </summary>
    internal static class InclusionGate
    {
        /// <summary>The <c>[IncludedWhen]</c> predicate on <paramref name="member" />, or <c>null</c> if ungated.</summary>
        public static string? ReadPredicate(MemberInfo member)
        {
            return member.GetCustomAttribute<IncludedWhenAttribute>()?.Predicate;
        }

        /// <summary>
        ///     Whether a member carrying <paramref name="predicate" /> is part of the configured instance.
        ///     <see cref="BindingMode.Definition" /> and ungated members are always included.
        ///     <see cref="BindingMode.Live" /> evaluates the predicate strict / fail-closed against
        ///     <paramref name="parameterContext" /> — a parse error (<see cref="PredicateSyntaxException" />)
        ///     or an evaluation error (<see cref="PredicateEvaluationException" />: missing/null/type-mismatched
        ///     value) propagates and fails <c>Configure</c>.
        /// </summary>
        public static bool IsIncluded(string? predicate, BindingMode mode, IReadOnlyDictionary<string, JsonNode?>? parameterContext)
        {
            if (predicate is null || mode == BindingMode.Definition)
            {
                return true;
            }

            return Predicate.Parse(predicate).Evaluate(parameterContext ?? EmptyContext);
        }

        /// <summary>
        ///     Builds the evaluator context from the block's <c>[InstantiationParameter]</c> properties in
        ///     the same JSON-scalar form the conformance vector, cloud, and dashboard use — enums as
        ///     member-name strings, integers as numbers — via <see cref="PropertyValueCodec.ClrToJson" />
        ///     (never <c>(int)</c> casts or raw <c>ToString()</c>). Reads whatever value each CLR property
        ///     currently holds (the applied payload value, or the C# initializer default when none was
        ///     supplied).
        /// </summary>
        public static IReadOnlyDictionary<string, JsonNode?> BuildParameterContext(object logicBlock)
        {
            var context = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

            foreach (var property in logicBlock.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<InstantiationParameterAttribute>() is null)
                {
                    continue;
                }

                var value = property.GetValue(logicBlock);

                // A null value is passed through as JSON null (never forced through the non-nullable codec
                // path, which would throw); a gate that then references it fails closed at Evaluate.
                context[property.Name] = value is null ? null : PropertyValueCodec.ClrToJson(value, TypeRefBuilder.BuildForProperty(property));
            }

            return context;
        }

        private static readonly IReadOnlyDictionary<string, JsonNode?> EmptyContext = new Dictionary<string, JsonNode?>(0);
    }
}
