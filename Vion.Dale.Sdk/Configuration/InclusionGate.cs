using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Generators.Predicates;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Configuration
{
    /// <summary>
    ///     Shared resolution of <see cref="IncludedWhenAttribute" /> gates for the three declarative
    ///     binders. One authority so interface, contract, and service binders resolve the
    ///     same gate identically for a given instance.
    /// </summary>
    internal static class InclusionGate
    {
        private static readonly IReadOnlyDictionary<string, JsonNode?> EmptyContext = new Dictionary<string, JsonNode?>(0);

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
            if (predicate is null)
            {
                return true;
            }

            if (mode == BindingMode.Definition)
            {
                return true;
            }

            return Predicate.Parse(predicate).Evaluate(parameterContext ?? EmptyContext);
        }

        /// <summary>
        ///     Refuses a gate that could never decide anything, in <see cref="BindingMode.Definition" />, where
        ///     no configuration exists to evaluate against. A predicate that does not parse, or that names
        ///     something this block does not declare as an <see cref="InstantiationParameterAttribute" />,
        ///     fails every activation — so the definition view refuses it rather than emitting it to the wire
        ///     and letting <c>dotnet pack</c> ship a block that can never start.
        ///     <para>
        ///         The referenced names are read <b>syntactically</b>, off the parsed tree, and the predicate is
        ///         never evaluated: an evaluator short-circuits, so <c>Count &gt;= 2 &amp;&amp; Missing &gt;= 1</c>
        ///         returns a verdict without ever reaching the undeclared name. Only whether a name is declared
        ///         is decided here; whether its type fits the literal it is compared against is DALE043's.
        ///     </para>
        /// </summary>
        public static void EnsureResolvable(string predicate, object logicBlock, string memberName)
        {
            try
            {
                Predicate.Parse(predicate);
            }
            catch (PredicateException exception)
            {
                throw Unresolvable(predicate, logicBlock, memberName, exception.Message, exception);
            }

            var parsed = PredicateParser.Parse(predicate);
            if (parsed.Ast is null)
            {
                // Vion.Contracts parsed it and the linked parser did not. Both implement the one grammar
                // predicate-conformance.json pins, so a disagreement means one of them is wrong about this
                // predicate — refusing keeps the gate from shipping unchecked while that is true.
                throw Unresolvable(predicate, logicBlock, memberName, parsed.Error!, null);
            }

            var declared = DeclaredParameterNames(logicBlock);
            var referenced = new List<string>();
            CollectReferences(parsed.Ast, referenced);

            foreach (var reference in referenced)
            {
                if (!declared.Contains(reference))
                {
                    throw Unresolvable(predicate, logicBlock, memberName, $"'{reference}' is not an [InstantiationParameter] of this block", null);
                }
            }
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

        // Every reference the predicate carries, whatever the tree's shape. A qualified reference contributes
        // its whole two-segment text, which no property name can match — inclusion gates are bare-ref only.
        private static void CollectReferences(PredicateNode node, ICollection<string> names)
        {
            switch (node)
            {
                case OrNode or:
                    CollectReferences(or.Left, names);
                    CollectReferences(or.Right, names);
                    break;
                case AndNode and:
                    CollectReferences(and.Left, names);
                    CollectReferences(and.Right, names);
                    break;
                case NotNode not:
                    CollectReferences(not.Operand, names);
                    break;
                case ComparisonNode comparison:
                    names.Add(comparison.Reference.Text);
                    break;
                case MembershipNode membership:
                    names.Add(membership.Reference.Text);
                    break;
                case BoolRefNode boolRef:
                    names.Add(boolRef.Reference.Text);
                    break;
                default:
                    // A node kind the grammar grew and this walk did not: skipping it would pass an unchecked
                    // reference through, which is the hole this method exists to close.
                    throw new InvalidOperationException($"Inclusion gates cannot check a predicate node of type '{node.GetType().Name}'.");
            }
        }

        // Ordinal, matching the context BuildParameterContext hands the evaluator: a gate naming 'pointcount'
        // resolves against nothing at bind, so it must not resolve here either.
        private static HashSet<string> DeclaredParameterNames(object logicBlock)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in logicBlock.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<InstantiationParameterAttribute>() is not null)
                {
                    names.Add(property.Name);
                }
            }

            return names;
        }

        private static InvalidOperationException Unresolvable(string predicate, object logicBlock, string memberName, string reason, Exception? inner)
        {
            return new
                InvalidOperationException($"Member '{memberName}' on logic block '{logicBlock.GetType().FullName}' has an [IncludedWhen] gate \"{predicate}\" that cannot be " +
                                          $"resolved against the block's [InstantiationParameter] properties: {reason}. The gate would fail every activation.",
                                          inner);
        }
    }
}