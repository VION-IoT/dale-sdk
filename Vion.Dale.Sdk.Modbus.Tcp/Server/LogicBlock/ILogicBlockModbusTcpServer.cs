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
    ///         The server accepts requests for any unit identifier and echoes the request's unit identifier in
    ///         the response — the endpoint behavior the Modbus TCP specification intends for directly connected
    ///         servers. Client requests outside the declared register-map extents are answered with an
    ///         IllegalDataAddress Modbus exception.
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
        /// </remarks>
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
        ///     Thrown when the port number is outside the valid range (0-65535).
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
        ///     snapshot accessors enforce the same bounds.
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
        ///     Gets the time of the most recent client write to any register area, or <c>null</c> when no client
        ///     has written yet.
        /// </summary>
        /// <remarks>
        ///     Maintained by the SDK from the underlying server's change notifications — no events are surfaced
        ///     to the logic block. Useful for communication surveillance (e.g. detecting a silent master).
        /// </remarks>
        DateTimeOffset? LastClientWriteAt { get; }

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
        /// </remarks>
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
        T Sync<T>(Func<IModbusServerSnapshot, T> access);
    }
}