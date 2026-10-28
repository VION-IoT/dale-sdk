using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    /// <summary>
    ///     One unit identifier a Modbus TCP server serves from a register map of its own, declared with
    ///     <see cref="ILogicBlockModbusTcpServer.AddUnit" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A unit is configured and read exactly like the server's own map: four extents, set while the server is
    ///         disabled, and all register access inside <see cref="Sync(Action{IModbusServerSnapshot})" /> callbacks.
    ///         A value written at one unit, by the block or by a client, is not seen at another.
    ///     </para>
    ///     <para>
    ///         Every unit's <c>Sync</c> takes the same server lock as the server's own. A callback may call another
    ///         unit's <c>Sync</c> to change several units atomically with respect to client requests.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface ILogicBlockModbusTcpServerUnit
    {
        /// <summary>
        ///     Gets the unit identifier this map is served under.
        /// </summary>
        byte UnitId { get; }

        /// <summary>
        ///     Gets or sets this unit's declared holding register extent: addresses 0 to count - 1 are served.
        ///     Default is 0 (the area is not served).
        /// </summary>
        /// <remarks>
        ///     Counts are extents, not sizes from an offset — a 10-register map at address 0x8000 declares 0x800A.
        ///     A client request outside the extent is answered with an IllegalDataAddress Modbus exception;
        ///     snapshot accessors enforce the same bounds.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        ushort HoldingRegisterCount { get; set; }

        /// <summary>
        ///     Gets or sets this unit's declared input register extent: addresses 0 to count - 1 are served.
        ///     Default is 0 (the area is not served).
        /// </summary>
        /// <remarks>
        ///     See <see cref="HoldingRegisterCount" /> for the extent semantics.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        ushort InputRegisterCount { get; set; }

        /// <summary>
        ///     Gets or sets this unit's declared coil extent: addresses 0 to count - 1 are served.
        ///     Default is 0 (the area is not served).
        /// </summary>
        /// <remarks>
        ///     See <see cref="HoldingRegisterCount" /> for the extent semantics.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        ushort CoilCount { get; set; }

        /// <summary>
        ///     Gets or sets this unit's declared discrete input extent: addresses 0 to count - 1 are served.
        ///     Default is 0 (the area is not served).
        /// </summary>
        /// <remarks>
        ///     See <see cref="HoldingRegisterCount" /> for the extent semantics.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        ushort DiscreteInputCount { get; set; }

        /// <summary>
        ///     Gets the time of the most recent client write to any area of this unit, or <c>null</c> when no client
        ///     has written to it yet.
        /// </summary>
        /// <remarks>
        ///     A write that does not change the stored value counts, so a master cyclically re-asserting a setpoint
        ///     keeps moving it. A write at another unit leaves it where it was; the server's own
        ///     <see cref="ILogicBlockModbusTcpServer.LastClientWriteAt" /> moves on a write at any unit.
        /// </remarks>
        DateTimeOffset? LastClientWriteAt { get; }

        /// <summary>
        ///     Executes <paramref name="access" /> with a consistent view of this unit's four register areas.
        /// </summary>
        /// <param name="access">
        ///     The callback receiving the snapshot. It runs synchronously on the caller's thread while the server
        ///     lock is held — keep it short and free of blocking calls, and do not capture the snapshot outside
        ///     the callback.
        /// </param>
        /// <remarks>
        ///     The same execution semantics as <see cref="ILogicBlockModbusTcpServer.Sync(Action{IModbusServerSnapshot})" />,
        ///     bounded by this unit's extents: it works while the server is disabled, and setting
        ///     <see cref="ILogicBlockModbusTcpServer.IsEnabled" /> or disposing the server from inside the callback
        ///     throws <see cref="InvalidOperationException" />.
        /// </remarks>
        void Sync(Action<IModbusServerSnapshot> access);

        /// <summary>
        ///     Executes <paramref name="access" /> with a consistent view of this unit's four register areas and
        ///     returns its result.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="access">
        ///     The callback receiving the snapshot. It runs synchronously on the caller's thread while the server
        ///     lock is held — keep it short and free of blocking calls, and do not capture the snapshot outside
        ///     the callback.
        /// </param>
        /// <returns>The value returned by <paramref name="access" />.</returns>
        /// <remarks>
        ///     See <see cref="Sync(Action{IModbusServerSnapshot})" /> for the execution semantics.
        /// </remarks>
        T Sync<T>(Func<IModbusServerSnapshot, T> access);
    }
}