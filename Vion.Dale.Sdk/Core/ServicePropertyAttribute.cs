using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Describe a service property on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    ///     <para>
    ///         A property MAY also carry <see cref="ServiceMeasuringPointAttribute" /> — the two are
    ///         <b>independent</b>. Each publishes to its own retained MQTT stream (<c>…/property/state</c>
    ///         vs <c>…/measuring-point/state</c>) and is throttled / deadbanded separately;
    ///         neither suppresses the other. Declaring both surfaces the same value as live state AND a
    ///         charted time series — common for telemetry (e.g. grid-meter power).
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     <see cref="Title" /> and <see cref="Description" /> are translatable in the cloud, keyed by the
    ///     block's full type name plus two <b>C# names</b>: the owning service (the logic-block class name,
    ///     or the holding property's name for a component service) and this property's name. There is no
    ///     <c>Identifier</c> override — renaming any of them orphans the translations. See
    ///     <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServicePropertyAttribute : Attribute, IThrottleConfigured
    {
        /// <summary>Display label for the property. Translatable (see the remarks on this attribute).</summary>
        public string? Title { get; init; }

        /// <summary>
        ///     Long-form description for tooltips, search, and accessibility. Routes into
        ///     <c>schema.description</c>. Independent of <see cref="Title" />, and separately
        ///     translatable (see the remarks on this attribute).
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
        ///     <c>schema.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }

        /// <summary>
        ///     Marks the property as read-only on the wire even when the C# property has a public setter.
        ///     Use this when a cross-assembly helper needs to assign the value (requires the public setter)
        ///     but the cloud must not be able to SetPropertyValue it back. Routes into
        ///     <c>schema.readOnly</c> — same wire flag that a private setter or a
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
        ///     (e.g. <c>"250ms"</c>, <c>"1s"</c>, <c>"500us"</c>) — a number with an optional
        ///     <c>us</c>/<c>ms</c>/<c>s</c>/<c>m</c>/<c>h</c> suffix; a bare number is milliseconds. Drives
        ///     the emission gate. <c>"0"</c> / <c>"0ms"</c> disables interval throttling. Defaults
        ///     to <c>"250ms"</c>. Validated by analyzers DALE036 (format) / DALE037 (1&#160;ms floor).
        /// </summary>
        public string MinInterval { get; init; } = "250ms";

        /// <summary>
        ///     Optional deadband: the minimum change a new value must clear (relative to the last emitted
        ///     value) before it is emitted. <b>The format depends on the property's type</b> — for the
        ///     built-in numeric types (<c>double</c>, <c>float</c>, <c>decimal</c>, <c>int</c>, <c>long</c>)
        ///     it is an invariant-culture number (e.g. <c>"0.1"</c>); for <c>TimeSpan</c> it is a duration
        ///     (e.g. <c>"1s"</c>). Any other type must register an <c>IChangeThreshold&lt;T&gt;</c> that
        ///     defines its format; <c>bool</c> has no magnitude and is not supported. <c>null</c> (the
        ///     default) means no deadband — only the value-equality dedup floor runs. Validated by analyzers
        ///     DALE034 (type) / DALE035 (format).
        /// </summary>
        /// <remarks>
        ///     On an <c>ImmutableArray&lt;T&gt;</c> property, no deadband is needed to keep a rebuilt-but-identical
        ///     table off the wire — the dedup floor compares array content, so a table reassigned every cycle
        ///     emits only when a row actually changed. Reach for <c>MinChange</c> plus a custom
        ///     <c>IChangeThreshold&lt;ImmutableArray&lt;T&gt;&gt;</c> when rows should also be considered unchanged
        ///     within a per-field tolerance.
        /// </remarks>
        public string? MinChange { get; init; }

        /// <summary>
        ///     When <c>true</c>, every observed change of this property is emitted immediately, bypassing
        ///     the interval and change gates. Defaults to <c>false</c>.
        /// </summary>
        public bool Immediate { get; init; }
    }
}