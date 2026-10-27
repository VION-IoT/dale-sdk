using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Per-field annotations for a field of a flat struct used as a service-element value —
    ///     a positional record-struct constructor parameter.
    /// </summary>
    /// <remarks>
    ///     Constructor parameters only. Both readers of this attribute walk the struct's positional
    ///     constructor, and no diagnostic registers on a property, so a declaration on the generated
    ///     property compiled, emitted nothing and warned about nothing.
    ///     <para />
    ///     <see cref="Title" /> and <see cref="Description" /> are translatable in the cloud, keyed by the
    ///     struct's <b>short type name</b> (the namespace is not part of the key) and the field's
    ///     <b>camelCase wire name</b> — the constructor parameter's name, first letter lower-cased.
    ///     Renaming either orphans the translations. See <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Parameter)]
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
        ///     Marks this field as a secret on the wire, while the struct's other fields stay visible. Where the
        ///     struct is a service property's type, the host runtime publishes and answers this field as the
        ///     redaction sentinel (<c>"***"</c>), a null staying null, and keeps the value it holds when a client
        ///     writes the struct back with the sentinel in this field; the DevHost does the same on what it serves
        ///     a browser. The block holds the real value, and it is persisted unencrypted unless the property is
        ///     excluded with <c>[Persistent(Exclude = true)]</c>. A measuring point is never redacted. Restricted
        ///     to <c>string</c> / <c>string?</c> fields in v1. Routes into the field's <c>schema.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }
    }
}