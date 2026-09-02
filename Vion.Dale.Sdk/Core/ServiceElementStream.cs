namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Which of a member's two publication streams is being built. A single C# property may carry
    ///     both <see cref="ServicePropertyAttribute" /> and <see cref="ServiceMeasuringPointAttribute" />,
    ///     and the two are independent: each has its own retained topic and its own emission knobs. Every
    ///     site that reads the knobs off a <c>PropertyInfo</c> takes this so it reads the attribute
    ///     belonging to the stream it is building, instead of whichever attribute is found first.
    /// </summary>
    internal enum ServiceElementStream
    {
        /// <summary>The <see cref="ServicePropertyAttribute" /> stream.</summary>
        Property,

        /// <summary>The <see cref="ServiceMeasuringPointAttribute" /> stream.</summary>
        MeasuringPoint,
    }
}
