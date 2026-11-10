using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The status byte of a meter's telegram header.
    /// </summary>
    /// <param name="Value">The status byte as received.</param>
    [PublicApi]
    public readonly record struct MBusStatus(byte Value)
    {
        /// <summary>
        ///     Whether the meter reports an error: its application status reads "any application error" or "abnormal
        ///     condition", or its permanent or temporary error bit is set. The manufacturer-specific bits and the
        ///     power-low bit do not count.
        /// </summary>
        public bool HasError
        {
            get => (Value & 0x03) >= 0x02 || (Value & 0x18) != 0;
        }
    }
}
