using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Define a measuring point on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceMeasuringPointAttribute : Attribute
    {
        public string? Title { get; init; }

        /// <summary>
        ///     Long-form description for tooltips, search, and accessibility. Routes into
        ///     <c>schema.annotations.description</c>. Independent of <see cref="Title" />.
        /// </summary>
        public string? Description { get; init; }

        public string? Unit { get; init; }

        /// <summary>
        ///     Advisory JSON-Schema <c>format</c> for a <c>string</c> measuring point (e.g.
        ///     <see cref="StringFormats.Ipv4" />). Routes into <c>schema.format</c>; drives a specialized
        ///     input + soft-validation in the dashboard / DevHost. Never enforced on the wire.
        ///     String-only and not a type-kind format (<c>date-time</c> / <c>duration</c> / <c>uuid</c>) —
        ///     see DALE033.
        /// </summary>
        public string? StringFormat { get; init; }

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;

        /// <summary>
        ///     Semantic classification of the measuring point's time-series shape — drives
        ///     default chart rendering, aggregation, and storage strategy. Routes into
        ///     <c>schema.annotations.x-kind</c>. Defaults to
        ///     <see cref="MeasuringPointKind.Measurement" /> (instantaneous samples).
        /// </summary>
        public MeasuringPointKind Kind { get; init; } = MeasuringPointKind.Measurement;

        [Obsolete("Use Title instead. Will be removed in next major.")]
        public string? DefaultName
        {
            get => Title;

            init => Title = value;
        }
    }
}