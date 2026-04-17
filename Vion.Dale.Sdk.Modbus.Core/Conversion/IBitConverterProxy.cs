namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Provides an abstraction over <see cref="System.BitConverter" /> for testability.
    /// </summary>
    public interface IBitConverterProxy
    {
        /// <summary>
        ///     Gets a value indicating whether the system architecture is little-endian.
        /// </summary>
        bool IsLittleEndian { get; }

        /// <summary>
        ///     Converts an unsigned 16-bit integer to a byte array.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A byte array containing the converted value.</returns>
        byte[] GetBytes(ushort value);

        /// <summary>
        ///     Converts a signed 16-bit integer to a byte array.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A byte array containing the converted value.</returns>
        byte[] GetBytes(short value);
    }
}