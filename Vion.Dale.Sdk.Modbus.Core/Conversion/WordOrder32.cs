using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Specifies the word order for 32-bit values composed of two 16-bit words.
    /// </summary>
    [PublicApi]
    public enum WordOrder32
    {
        /// <summary>
        ///     Most significant word first (big-endian word order).
        /// </summary>
        MswToLsw = 0,

        /// <summary>
        ///     Least significant word first (little-endian word order).
        /// </summary>
        LswToMsw = 1,
    }
}