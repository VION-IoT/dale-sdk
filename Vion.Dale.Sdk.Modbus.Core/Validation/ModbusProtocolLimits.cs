using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Validation
{
    /// <summary>
    ///     The most a single Modbus request can carry, per function code. These are limits of the protocol's
    ///     data unit, not of any device: no standard function code has a field wide enough to answer more.
    /// </summary>
    /// <remarks>
    ///     A client refuses a read or write past its limit before the request reaches the wire, with an
    ///     <see cref="Exceptions.InvalidCountException" /> and an <c>Outcome.Invalid</c> receipt. Split a larger
    ///     span into several requests; the addresses stay contiguous, so the block sees one value either way.
    /// </remarks>
    [PublicApi]
    public static class ModbusProtocolLimits
    {
        /// <summary>
        ///     Registers a single read of holding or input registers can return (function codes 3 and 4).
        /// </summary>
        public const int MaxRegistersPerRead = 125;

        /// <summary>
        ///     Registers a single write of multiple holding registers can carry (function code 16).
        /// </summary>
        public const int MaxRegistersPerWrite = 123;

        /// <summary>
        ///     Bits a single read of coils or discrete inputs can return (function codes 1 and 2).
        /// </summary>
        public const int MaxBitsPerRead = 2000;

        /// <summary>
        ///     Bits a single write of multiple coils can carry (function code 15).
        /// </summary>
        public const int MaxBitsPerWrite = 1968;
    }
}