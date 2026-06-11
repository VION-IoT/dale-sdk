using System;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     Provides access to a live server register buffer. Invoked per operation so that accessors never
    ///     hold on to a <see cref="Span{T}" /> (which cannot be stored) and always see the current buffer.
    /// </summary>
    /// <returns>The buffer of one register area as raw bytes, in Modbus wire order (big-endian per 16-bit word).</returns>
    public delegate Span<byte> ModbusServerBufferAccessor();
}
