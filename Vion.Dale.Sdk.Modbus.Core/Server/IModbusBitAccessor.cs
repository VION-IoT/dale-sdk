using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     Typed access to one bit-addressed server area (coils or discrete inputs) inside a server snapshot.
    /// </summary>
    /// <remarks>
    ///     Only valid inside the snapshot callback that provided it — the server lock is held for the duration
    ///     of the callback and released afterwards. Do not capture accessors outside the callback.
    /// </remarks>
    [PublicApi]
    public interface IModbusBitAccessor
    {
        /// <summary>
        ///     Reads a single bit.
        /// </summary>
        /// <param name="address">The bit address to read.</param>
        /// <returns>The bit value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when <paramref name="address" /> lies outside the declared extent of the area.
        /// </exception>
        bool Read(ushort address);

        /// <summary>
        ///     Writes a single bit.
        /// </summary>
        /// <param name="address">The bit address to write.</param>
        /// <param name="value">The bit value to write.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when <paramref name="address" /> lies outside the declared extent of the area.
        /// </exception>
        void Write(ushort address, bool value);
    }
}