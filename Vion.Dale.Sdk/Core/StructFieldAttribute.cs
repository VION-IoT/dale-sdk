using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Per-field annotations for fields of a flat struct used as a service-element value.
    ///     Applies to positional record-struct constructor parameters (preferred) or properties.
    /// </summary>
    /// <remarks>
    ///     <see cref="Title" /> and <see cref="Description" /> are translatable in the cloud, keyed by the
    ///     struct's <b>short type name</b> (the wire's <c>schema.title</c> — the namespace never travels)
    ///     and the field's <b>camelCase wire name</b>, which is the constructor parameter's name with a
    ///     lower-cased first letter. Renaming the struct type orphans every field's translations; renaming
    ///     a parameter orphans that field's. See <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public class StructFieldAttribute : Attribute
    {
        /// <summary>
        ///     Display label for the field. Translatable (see the remarks on this attribute); the value
        ///     here is the source default and the permanent fallback.
        /// </summary>
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