using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    /// <summary>
    ///     Hosts a Modbus TCP server (slave role) for logic blocks: external Modbus clients connect to the
    ///     logic block and read or write its register map.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The server is configured via properties and gated by <see cref="IsEnabled" />, exactly like the
    ///         Modbus TCP client: configure while disabled, then enable. Configuration properties can only be
    ///         changed while the server is disabled — to reconfigure, disable the server first, update the
    ///         settings, then re-enable it (a port or address change is a rebind).
    ///     </para>
    ///     <para>
    ///         It listens on all interfaces (<c>0.0.0.0</c>) unless told otherwise, because a logic-block-hosted
    ///         server exists to be reached by Modbus masters elsewhere on the network. Set
    ///         <see cref="ListenAddress" /> to bind one interface — loopback for a simulator that must not be
    ///         reachable off the machine, or a specific NIC on a multi-homed device.
    ///     </para>
    ///     <para>
    ///         All register access happens inside <see cref="Sync(Action{IModbusServerSnapshot})" /> callbacks,
    ///         which execute synchronously on the caller's thread while holding the server lock — client requests
    ///         are served from the register buffers on background threads, and the lock makes each callback atomic
    ///         with respect to them. No events or callbacks are ever delivered to the logic block from background
    ///         threads. The block chooses its own cadence (a timer, a reactive trigger) and may use several
    ///         independent cadences; each <c>Sync</c> call is atomic on its own.
    ///     </para>
    ///     <para>
    ///         The register buffers exist independently of the listener: <c>Sync</c> also works while the server
    ///         is disabled, so a block can seed default values before it starts listening. Buffer contents survive
    ///         disable/enable cycles.
    ///     </para>
    ///     <para>
    ///         A server that declares no unit accepts requests for any unit identifier, answers them from its one
    ///         register map and echoes the request's unit identifier in the response — the endpoint behavior the
    ///         Modbus TCP specification intends for directly connected servers. Client requests outside the declared
    ///         register-map extents are answered with an IllegalDataAddress Modbus exception.
    ///     </para>
    ///     <para>
    ///         A server that stands in for a device serving several unit identifiers behind one port — different
    ///         values at the same addresses under each — declares each with <see cref="AddUnit" /> instead, and
    ///         configures and reads each unit through the handle it returns. A request to a unit identifier it
    ///         did not declare is then answered with a GatewayTargetDeviceFailedToRespond Modbus exception, and the
    ///         connection stays open.
    ///     </para>
    ///     <para>
    ///         The server should be disposed when no longer needed. Disposal stops the listener and releases all
    ///         resources; benign teardown races of the underlying server library are caught and logged, never
    ///         thrown.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface ILogicBlockModbusTcpServer : IDisposable
    {
        /// <summary>
        ///     Gets or sets whether the server is enabled. Default is <c>false</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Setting <c>true</c> binds the listener and starts serving client requests; a bind failure (e.g.
        ///         the port is already in use) throws and the server stays disabled — the logic block decides
        ///         whether that is fatal. Setting <c>false</c> stops the listener; register contents are retained.
        ///     </para>
        ///     <para>
        ///         Configuration properties can only be changed while disabled. Setting the same value again is a
        ///         no-op.
        ///     </para>
        ///     <para>
        ///         Enabling a server that declares units throws <see cref="InvalidOperationException" /> when any
        ///         server-level extent is non-zero: those extents bound a map such a server does not serve.
        ///     </para>
        ///     <para>
        ///         Must not be set from inside a <see cref="Sync(Action{IModbusServerSnapshot})" /> callback —
        ///         stopping the listener joins request-handler threads that may be waiting for the server lock
        ///         the callback holds. Doing so throws <see cref="InvalidOperationException" />; react to
        ///         client-written commands after the callback returns.
        ///     </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when set from inside a <c>Sync</c> callback, the server's or a unit's, or when enabling a
        ///     server that declares units while a server-level extent is non-zero.
        /// </exception>
        bool IsEnabled { get; set; }

        /// <summary>
        ///     Gets or sets the local IP address the server listens on. Default is <c>"0.0.0.0"</c> (all interfaces).
        /// </summary>
        /// <exception cref="FormatException">
        ///     Thrown when the value is null, empty, consists only of whitespace, or is not a valid IP address.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        string? ListenAddress { get; set; }

        /// <summary>
        ///     Gets or sets the local port the server listens on. Default is 502 (standard Modbus TCP port).
        /// </summary>
        /// <exception cref="FormatException">
        ///     Thrown when the port number is outside the valid range (1-65535). Port 0 binds an ephemeral port the
        ///     block is never told, so no master can be pointed at it.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        int Port { get; set; }

        /// <summary>
        ///     Gets or sets the declared holding register extent: addresses 0 to count - 1 are served.
        ///     Default is 0 (the area is not served).
        /// </summary>
        /// <remarks>
        ///     Counts are extents, not sizes from an offset — a 10-register map at address 0x8000 declares 0x800A.
        ///     Client requests outside the extent are answered with an IllegalDataAddress Modbus exception;
        ///     snapshot accessors enforce the same bounds. The four server-level extents bound the one map of a
        ///     server that declares no unit; a server that declares units leaves them at 0 and sets each unit's
        ///     own.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server is enabled. Disable, reconfigure, then re-enable.
        /// </exception>
        ushort HoldingRegisterCount { get; set; }

        /// <summary>
        ///     Gets or sets the declared input register extent: addresses 0 to count - 1 are served.
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
        ///     Gets or sets the declared coil extent: addresses 0 to count - 1 are served.
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
        ///     Gets or sets the declared discrete input extent: addresses 0 to count - 1 are served.
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
        ///     Gets a value indicating whether the server is currently listening for client connections.
        /// </summary>
        bool IsListening { get; }

        /// <summary>
        ///     Gets the number of currently connected clients.
        /// </summary>
        int ConnectionCount { get; }

        /// <summary>
        ///     Gets the time of the most recent client write to any register area, at any unit, or <c>null</c> when
        ///     no client has written yet.
        /// </summary>
        /// <remarks>
        ///     Maintained by the SDK from the underlying server's change notifications — no events are surfaced
        ///     to the logic block. Useful for communication surveillance (e.g. detecting a silent master). A server
        ///     that declares units also keeps one per unit, <see cref="ILogicBlockModbusTcpServerUnit.LastClientWriteAt" />.
        /// </remarks>
        DateTimeOffset? LastClientWriteAt { get; }

        /// <summary>
        ///     Declares a unit identifier this server serves from a register map of its own, and returns the handle
        ///     that configures and reads that map.
        /// </summary>
        /// <param name="unitId">The unit identifier to serve, 0 to 255.</param>
        /// <returns>The unit's handle: its extents, its <c>Sync</c> and its last client write.</returns>
        /// <remarks>
        ///     <para>
        ///         Declaring the first unit is the opt-in: the server then answers each declared unit identifier
        ///         from that unit's own map, bounded by that unit's own extents, and answers a unit identifier it
        ///         did not declare with a GatewayTargetDeviceFailedToRespond Modbus exception, keeping the
        ///         connection open. Declare every unit while the server is disabled; a unit cannot be removed —
        ///         a block that needs another layout creates another server.
        ///     </para>
        ///     <para>
        ///         A server that declares units has no map of its own: its <c>Sync</c> throws, and its four
        ///         extents stay 0.
        ///     </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when the server is enabled.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="unitId" /> is already declared.</exception>
        ILogicBlockModbusTcpServerUnit AddUnit(byte unitId);

        /// <summary>
        ///     Executes <paramref name="access" /> with a consistent view of all four register areas.
        /// </summary>
        /// <param name="access">
        ///     The callback receiving the snapshot. It runs synchronously on the caller's thread while the server
        ///     lock is held — keep it short and free of blocking calls, and do not capture the snapshot outside
        ///     the callback.
        /// </param>
        /// <remarks>
        ///     All buffer access for one cycle should happen in a single call, making read-modify-publish patterns
        ///     (e.g. echoing a client-written heartbeat into a feedback register) atomic with respect to client
        ///     requests. Also works while the server is disabled (e.g. to seed default values before enabling).
        ///     Do not set <see cref="IsEnabled" /> or call <see cref="IDisposable.Dispose" /> from inside the
        ///     callback — both throw <see cref="InvalidOperationException" /> there (see <see cref="IsEnabled" />).
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server declares units, which have no shared map; sync through each unit's handle.
        /// </exception>
        void Sync(Action<IModbusServerSnapshot> access);

        /// <summary>
        ///     Executes <paramref name="access" /> with a consistent view of all four register areas and returns
        ///     its result.
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
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the server declares units, which have no shared map; sync through each unit's handle.
        /// </exception>
        T Sync<T>(Func<IModbusServerSnapshot, T> access);
    }
}