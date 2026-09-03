using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Introspection
{
    /// <summary>
    ///     Builds the <c>presentation.fields</c> node: the per-struct-field label / value-label /
    ///     severity data that has no inline home on the field's own schema (VION-105).
    ///     Three annotations authored on a field of a published <c>readonly record struct</c> cannot
    ///     travel inline:
    ///     <list type="bullet">
    ///         <item>
    ///             <c>[StructField(Title = …)]</c> on an enum- or struct-typed field — the field's
    ///             <c>schema.title</c> already carries the CLR type name, which is the cloud's
    ///             translation key, and <c>Vion.Contracts</c>' <c>ApplyAnnotations</c> keeps it.
    ///         </item>
    ///         <item><c>[EnumLabel]</c> and <c>[Severity]</c> on the field's enum type — JSON Schema has no slot for either.</item>
    ///     </list>
    ///     Emitted as a sibling map keyed by the field's camelCase wire key and shaped like the
    ///     property-level presentation node (<c>displayName</c> / <c>enumLabels</c> /
    ///     <c>statusMappings</c>), then injected into the serialized <c>presentation</c> document by
    ///     <see cref="LogicBlockIntrospection" /> — the same opaque-passthrough trick
    ///     <c>ApplyInstantiationParameterRuntime</c> uses on <c>runtime</c>, and for the same reason:
    ///     <c>Presentation</c> is a sealed contracts record with a hand-written serializer, and nothing
    ///     downstream re-serializes the node (cloud-api stores and serves it as a bare
    ///     <c>JsonNode?</c>; the dale runtime never parses it).
    /// </summary>
    internal static class StructFieldPresentationBuilder
    {
        /// <summary>
        ///     Builds the <c>fields</c> map for a property whose CLR type is (or wraps) a flat
        ///     <c>readonly record struct</c>. Returns <c>null</c> for a non-struct property and for a
        ///     struct no field of which carries any of the three annotations — so an otherwise-empty
        ///     <c>presentation</c> keeps serializing to JSON null.
        /// </summary>
        public static JsonObject? Build(Type propertyType)
        {
            var structType = TypeRefBuilder.ExtractStructType(propertyType);
            if (structType is null)
            {
                return null;
            }

            // Same primary positional constructor TypeRefBuilder.BuildStructFieldAnnotations walks —
            // deliberately WITHOUT its `[StructField]` filter: labels and severities belong to the field's
            // enum type, so an unannotated field must still get them.
            var ctor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault(c => c.GetParameters().Length > 0);

            if (ctor is null)
            {
                return null;
            }

            var fields = new JsonObject();
            foreach (var parameter in ctor.GetParameters())
            {
                var entry = BuildFieldEntry(parameter);
                if (entry is not null)
                {
                    fields[TypeRefBuilder.ToCamelCase(parameter.Name!)] = entry;
                }
            }

            return fields.Count > 0 ? fields : null;
        }

        private static JsonObject? BuildFieldEntry(ParameterInfo parameter)
        {
            var entry = new JsonObject();

            // displayName only where the inline slot cannot carry the title: for a scalar field the
            // authored Title already lands in schema.title, and duplicating it here would leave two
            // sources with no rule about which wins. An authored empty title is still authored — a
            // length test here would drop it while the same empty title on a scalar field lands inline,
            // so the one place a title is re-routed would be the one place it is not carried verbatim.
            var structField = parameter.GetCustomAttribute<StructFieldAttribute>();
            if (structField?.Title is { } title && PropertyMetadataBuilder.HasIdentityBearingTitle(TypeRefBuilder.BuildForStructField(parameter)))
            {
                entry["displayName"] = JsonValue.Create(title);
            }

            // Read off the field's enum type. Nullable<TEnum> is peeled by the extractors themselves —
            // ModbusLinkSummary.LastFailureOutcome is a ModbusOutcome?, so this is not hypothetical.
            // Severities are ungated here: the property-level StatusIndicator gate exists because that
            // flag also routes a property to a status tile, a meaning a struct field does not have.
            AddMap(entry, "enumLabels", PropertyMetadataBuilder.ExtractEnumLabels(parameter.ParameterType));
            AddMap(entry, "statusMappings", PropertyMetadataBuilder.ExtractStatusMappings(parameter.ParameterType));

            return entry.Count > 0 ? entry : null;
        }

        private static void AddMap(JsonObject entry, string key, ImmutableDictionary<string, string>? map)
        {
            if (map is null)
            {
                return;
            }

            var node = new JsonObject();
            foreach (var pair in map)
            {
                node[pair.Key] = JsonValue.Create(pair.Value);
            }

            entry[key] = node;
        }
    }
}