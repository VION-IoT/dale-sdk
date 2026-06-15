using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Describe a service property on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServicePropertyAttribute : Attribute
    {
        public string? Title { get; init; }

        /// <summary>
        ///     Long-form description for tooltips, search, and accessibility. Routes into
        ///     <c>schema.annotations.description</c>. Independent of <see cref="Title" />.
        /// </summary>
        public string? Description { get; init; }

        public string? Unit { get; init; }

        /// <summary>
        ///     Advisory JSON-Schema <c>format</c> for a <c>string</c> value (e.g.
        ///     <see cref="StringFormats.Ipv4" />). Routes into <c>schema.format</c>; drives a specialized
        ///     input + soft-validation in the dashboard / DevHost. Never enforced on the wire.
        ///     String-only and not a type-kind format (<c>date-time</c> / <c>duration</c> / <c>uuid</c>) —
        ///     see DALE033.
        /// </summary>
        public string? StringFormat { get; init; }

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;

        /// <summary>
        ///     Marks a writable property as a secret — clients see a redaction sentinel
        ///     (<c>"***"</c>) on the publish-state channel instead of the actual value.
        ///     Restricted to <c>string</c> / <c>string?</c> properties in v1. Routes into
        ///     <c>schema.annotations.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }

        /// <summary>
        ///     Marks the property as read-only on the wire even when the C# property has a public setter.
        ///     Use this when a cross-assembly helper needs to assign the value (requires the public setter)
        ///     but the cloud must not be able to SetPropertyValue it back. Routes into
        ///     <c>schema.annotations.readOnly</c> — same wire flag that a private setter or a
        ///     <c>[ServiceMeasuringPoint]</c> would set, so the dashboard groups it with metrics.
        /// </summary>
        public bool ReadOnly { get; init; }

        [Obsolete("Use Title instead. Will be removed in next major.")]
        public string? DefaultName
        {
            get => Title;

            init => Title = value;
        }

        [Obsolete("Use Minimum instead. Will be removed in next major.")]
        public double MinValue
        {
            get => Minimum;

            init => Minimum = value;
        }

        [Obsolete("Use Maximum instead. Will be removed in next major.")]
        public double MaxValue
        {
            get => Maximum;

            init => Maximum = value;
        }
    }
}