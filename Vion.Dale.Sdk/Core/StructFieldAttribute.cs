using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Per-field annotations for fields of a flat struct used as a service-element value.
    ///     Applies to positional record-struct constructor parameters (preferred) or properties.
    /// </summary>
    /// <remarks>
    ///     <see cref="Title" /> and <see cref="Description" /> are translatable in the cloud, keyed by the
    ///     struct's <b>short type name</b> (the namespace is not part of the key) and the field's
    ///     <b>camelCase wire name</b> — the constructor parameter's name, first letter lower-cased.
    ///     Renaming either orphans the translations. See <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public class StructFieldAttribute : Attribute
    {
        /// <summary>Display label for the field. Translatable (see the remarks on this attribute).</summary>
        public string? Title { get; init; }

        /// <summary>Long-form description for the field. Separately translatable, like <see cref="Title" />.</summary>
        public string? Description { get; init; }

        public string? Unit { get; init; }

        /// <summary>
        ///     Advisory JSON-Schema <c>format</c> for a string field (e.g.
        ///     <see cref="StringFormats.Ipv4" />). Routes into the field's <c>schema.format</c>.
        ///     String-only — see DALE033.
        /// </summary>
        public string? StringFormat { get; init; }

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;

        /// <summary>
        ///     Marks this field as a secret — clients see a redaction sentinel (<c>"***"</c>) on the
        ///     publish-state channel instead of the actual value, while the struct's other fields stay
        ///     visible. Restricted to <c>string</c> / <c>string?</c> fields in v1. Routes into the
        ///     field's <c>schema.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }
    }
}