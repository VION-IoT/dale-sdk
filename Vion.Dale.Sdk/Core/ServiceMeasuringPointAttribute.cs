using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Define a measuring point on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ServiceMeasuringPointAttribute : Attribute
    {
        public string? Title { get; init; }

        public string? Unit { get; init; }

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;

        [Obsolete("Use Title instead. Will be removed in next major.")]
        public string? DefaultName
        {
            get => Title;

            init => Title = value;
        }
    }
}
