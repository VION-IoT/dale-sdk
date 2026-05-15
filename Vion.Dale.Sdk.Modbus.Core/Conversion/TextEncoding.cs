using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Specifies the text encoding format for string conversion.
    /// </summary>
    [PublicApi]
    public enum TextEncoding
    {
        /// <summary>
        ///     ASCII encoding (7-bit character set).
        /// </summary>
        Ascii = 0,

        /// <summary>
        ///     UTF-8 encoding (variable-length, 1-4 bytes per character).
        /// </summary>
        Utf8 = 1,

        /// <summary>
        ///     UTF-16 Little Endian encoding (2 or 4 bytes per character).
        /// </summary>
        Utf16Le = 2,

        /// <summary>
        ///     UTF-16 Big Endian encoding (2 or 4 bytes per character).
        /// </summary>
        Utf16Be = 3,
    }
}