using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Per-field annotations for fields of a flat struct used as a service-element value.
    ///     Applies to positional record-struct constructor parameters (preferred) or properties.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public class StructFieldAttribute : Attribute
    {
        public string? Title { get; init; }

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
        ///     field's <c>schema.annotations.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }
    }
}