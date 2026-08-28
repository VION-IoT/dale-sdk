using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     Flattens a <c>[ScenarioWire]</c> wire struct into the scalar field leaves a scenario can address —
    ///     the vocabulary behind <c>serviceProviderExpect</c>'s <c>field</c> selector. Paths use the camelCase
    ///     wire keys the codec serializes and dot through a nested struct (<c>limits.activePowerW</c>), the same
    ///     shape <c>expect</c> uses for a struct-typed member, and resolve case-insensitively at both ends.
    ///     <para>
    ///         Both lists ride on the contract's <see cref="Control.ConfigurationOutput.LogicBlockContract.Annotations" />
    ///         — <see cref="OutputFieldsAnnotationKey" /> for the outbound command, <see cref="InputFieldsAnnotationKey" />
    ///         for the inbound the service provider delivers — so the resolver <em>and</em>
    ///         <c>dale scenario validate</c> — which sees only an exported configuration, never a loaded
    ///         assembly — can judge a step before a run. An <b>empty</b> list means the wire struct round-trips
    ///         as a bare scalar (the codec's single-field unwrap), so it has no addressable field.
    ///     </para>
    ///     <para>
    ///         The two keys carry different weight, because the two operations refuse for different reasons.
    ///         <see cref="OutputFieldsAnnotationKey" /> validates a <c>field</c> selector: <b>absent</b> means the
    ///         DevHost could not join the contract to a handler type, the static checks stand down, and the
    ///         runner's read-time guard is the only gate. <see cref="InputFieldsAnnotationKey" /> is itself the
    ///         drive gate: its <b>presence</b> is what makes a contract drivable with <c>serviceProviderSet</c>,
    ///         because nothing can be delivered to a block on a contract whose handler declares no inbound.
    ///     </para>
    /// </summary>
    internal static class ScenarioWireFields
    {
        /// <summary>The contract-annotation key carrying the inbound wire struct's field leaves — and the drive gate.</summary>
        public const string InputFieldsAnnotationKey = "scenarioInputFields";

        /// <summary>The contract-annotation key carrying the outbound command's addressable field leaves.</summary>
        public const string OutputFieldsAnnotationKey = "scenarioOutputFields";

        /// <summary>
        ///     The addressable scalar leaves of <paramref name="wireStruct" />, in declaration order. Descends
        ///     nested wire structs; skips collection-typed fields, which are not comparable in v1 and therefore
        ///     not addressable.
        /// </summary>
        public static IReadOnlyList<string> LeafPaths(Type wireStruct)
        {
            var leaves = new List<string>();
            Collect(wireStruct, string.Empty, leaves);
            return leaves;
        }

        // Recursion terminates by construction: a value type cannot contain itself (CS0523), so a wire struct's
        // field graph is finite and acyclic — no depth cap is needed.
        private static void Collect(Type wireStruct, string prefix, List<string> leaves)
        {
            var constructor = wireStruct.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (constructor is null)
            {
                return;
            }

            foreach (var parameter in constructor.GetParameters())
            {
                var path = prefix + TypeRefBuilder.ToCamelCase(parameter.Name!);
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                if (IsCollection(type))
                {
                    continue;
                }

                // Only a readonly record struct is serialized as a NESTED object; every other struct the wire
                // may carry (DateTimeOffset, TimeSpan, Guid) has a converter and lands as a scalar leaf.
                if (TypeRefBuilder.IsReadonlyRecordStruct(type))
                {
                    Collect(type, path + ".", leaves);
                }
                else
                {
                    leaves.Add(path);
                }
            }
        }

        private static bool IsCollection(Type type)
        {
            return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
        }
    }
}