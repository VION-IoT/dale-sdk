using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Describe a service property on a service interface or logic block property.
    ///     The optional properties become annotations in the introspection schema document.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ServicePropertyAttribute : Attribute
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
