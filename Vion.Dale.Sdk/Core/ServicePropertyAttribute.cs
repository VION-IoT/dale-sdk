using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Describe a service property on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServicePropertyAttribute : Attribute, IThrottleConfigured
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

        /// <summary>
        ///     Minimum spacing between two emitted values for this property, as a duration string
        ///     (e.g. <c>"250ms"</c>, <c>"1s"</c>, <c>"500us"</c>, <c>"0"</c>). Drives the RFC 0004
        ///     emission gate. <c>"0"</c> / <c>"0ms"</c> disables interval throttling. Defaults to
        ///     <c>"250ms"</c>.
        /// </summary>
        public string MinInterval { get; init; } = "250ms";

        /// <summary>
        ///     Optional minimum change (relative to the last emitted value) a new value must clear before
        ///     it is emitted, resolved against a registered change-threshold for the property's value type.
        ///     <c>null</c> (the default) means no change gate.
        /// </summary>
        public string? MinChange { get; init; }

        /// <summary>
        ///     When <c>true</c>, every observed change of this property is emitted immediately, bypassing
        ///     the interval and change gates. Defaults to <c>false</c>.
        /// </summary>
        public bool Immediate { get; init; }
    }
}