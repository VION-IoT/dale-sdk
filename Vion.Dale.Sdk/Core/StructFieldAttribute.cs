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

        public double Minimum { get; init; } = double.NegativeInfinity;

        public double Maximum { get; init; } = double.PositiveInfinity;
    }
}