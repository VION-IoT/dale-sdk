namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     An emission attribute as its author wrote it: the knob values, and which of them were assigned.
    ///     The public getters cannot say that — an omitted <c>MinInterval</c> reads the same as
    ///     <c>"250ms"</c> written out — and the knobs resolve one at a time, the first attribute that
    ///     assigned a knob supplying it.
    /// </summary>
    internal interface IEmissionAttribute : IThrottleConfigured
    {
        /// <summary>The knobs the declaration assigned, whatever values it gave them.</summary>
        EmissionKnob Assigned { get; }
    }
}
