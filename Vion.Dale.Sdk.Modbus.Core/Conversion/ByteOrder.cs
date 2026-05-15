using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Specifies the byte order for multibyte values.
    /// </summary>
    /// <remarks>
    ///     The Modbus protocol standard defines <see cref="MsbToLsb" /> (big-endian) as the standard byte order.
    ///     However, not all devices respect this standard, so both byte orders are supported.
    /// </remarks>
    [PublicApi]
    public enum ByteOrder
    {
        /// <summary>
        ///     Most significant byte first (big-endian).
        /// </summary>
        MsbToLsb = 0,

        /// <summary>
        ///     Least significant byte first (little-endian).
        /// </summary>
        LsbToMsb = 1,
    }
}