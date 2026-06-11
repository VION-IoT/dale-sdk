using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     The declared register-map extents of a Modbus server, per area.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Counts are extents, not sizes from an offset: addresses <c>0</c> to <c>Count - 1</c> are served.
    ///         A map that starts at an offset (e.g. a 10-register map at address 0x8000) declares
    ///         <c>offset + size</c> (0x800A). A count of 0 means the area is not served at all.
    ///     </para>
    ///     <para>
    ///         Extents drive request validation and accessor bounds checks — they do not size any buffer
    ///         (the underlying server buffers always cover the full Modbus address range).
    ///     </para>
    /// </remarks>
    /// <param name="HoldingRegisterCount">The number of addressable holding registers, starting at address 0.</param>
    /// <param name="InputRegisterCount">The number of addressable input registers, starting at address 0.</param>
    /// <param name="CoilCount">The number of addressable coils, starting at address 0.</param>
    /// <param name="DiscreteInputCount">The number of addressable discrete inputs, starting at address 0.</param>
    [PublicApi]
    public readonly record struct ModbusServerAreaExtents(ushort HoldingRegisterCount, ushort InputRegisterCount, ushort CoilCount, ushort DiscreteInputCount)
    {
        /// <summary>
        ///     Determines whether the given address range lies fully inside the declared extent of an area.
        /// </summary>
        /// <param name="area">The register area the range refers to.</param>
        /// <param name="startingAddress">The first address of the range.</param>
        /// <param name="quantity">The number of registers or bits in the range. Must be at least 1 to be coverable.</param>
        /// <returns><c>true</c> when the whole range is served; otherwise <c>false</c>.</returns>
        public bool Covers(ModbusServerArea area, ushort startingAddress, uint quantity)
        {
            var extent = area switch
            {
                ModbusServerArea.HoldingRegisters => HoldingRegisterCount,
                ModbusServerArea.InputRegisters => InputRegisterCount,
                ModbusServerArea.Coils => CoilCount,
                _ => DiscreteInputCount,
            };

            return quantity >= 1 && startingAddress + (long)quantity <= extent;
        }
    }
}
