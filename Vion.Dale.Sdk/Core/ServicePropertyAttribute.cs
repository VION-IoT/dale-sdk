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

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;

        /// <summary>
        ///     Marks a writable property as a secret — clients see a redaction sentinel
        ///     (<c>"***"</c>) on the publish-state channel instead of the actual value.
        ///     Restricted to <c>string</c> / <c>string?</c> properties in v1. Routes into
        ///     <c>schema.annotations.writeOnly</c>.
        /// </summary>
        public bool WriteOnly { get; init; }

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
