using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Which value of a quantity a data record carries, as its function field states.
    /// </summary>
    [PublicApi]
    public enum MBusFunction
    {
        /// <summary>The current value.</summary>
        Instantaneous,

        /// <summary>A maximum value.</summary>
        Maximum,

        /// <summary>A minimum value.</summary>
        Minimum,

        /// <summary>The value during an error state.</summary>
        ValueDuringError,
    }
}