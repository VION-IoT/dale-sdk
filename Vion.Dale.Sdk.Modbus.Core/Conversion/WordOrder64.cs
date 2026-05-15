// ReSharper disable InconsistentNaming

using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Specifies the word order for 64-bit values composed of four 16-bit words.
    /// </summary>
    [PublicApi]
    public enum WordOrder64
    {
        /// <summary>
        ///     Big-endian word order most significant word to least significant word (A is the most significant word).
        /// </summary>
        ABCD = 0,

        /// <summary>
        ///     Little-endian word order least significant to most significant word (D is the least significant word).
        /// </summary>
        DCBA = 1,

        /// <summary>
        ///     Mid-big-endian word order, big endian because when CD and AB are swapped it results in ABCD which is big-endian (A is the most significant word).
        /// </summary>
        /// <remarks>
        ///     This is uncommon but can occur in devices that don't natively support 64-bit values and where the words are not correctly stored in big or little-endian order.
        /// </remarks>
        CDAB = 2,

        /// <summary>
        ///     Mid-little-endian word order, little endian because when BA and DC are swapped it results in DCBA which is little-endian (D is the least significant word).
        /// </summary>
        /// <remarks>
        ///     This is uncommon but can occur in devices that don't natively support 64-bit values and where the words are not correctly stored in big or little-endian order.
        /// </remarks>
        BADC = 3,
    }
}