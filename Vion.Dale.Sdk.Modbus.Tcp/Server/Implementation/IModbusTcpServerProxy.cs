using System;
using System.Net;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    /// <summary>
    ///     Provides an abstraction for hosting a Modbus TCP server for testability.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The register buffers exist independently of the listener: the buffer accessors are valid before
    ///         <see cref="Start" /> and after <see cref="Stop" />, so a logic block can seed default values before
    ///         it starts listening, and buffer contents survive stop/start cycles.
    ///     </para>
    ///     <para>
    ///         Buffer access must hold <see cref="Lock" /> — once started, request handlers serve clients from
    ///         these buffers on background threads.
    ///     </para>
    /// </remarks>
    public interface IModbusTcpServerProxy : IDisposable
    {
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
        ///     has written since the server was created.
        /// </summary>
        /// <remarks>
        ///     Maintained internally from the server's change notifications — no events are surfaced to consumers.
        /// </remarks>
        DateTimeOffset? LastClientWriteAt { get; }

        /// <summary>
        ///     Gets the lock object guarding the register buffers against the background request handlers.
        /// </summary>
        object Lock { get; }

        /// <summary>
        ///     Binds the listener and starts serving client requests.
        /// </summary>
        /// <param name="listenAddress">The local address to bind to.</param>
        /// <param name="port">The local port to bind to.</param>
        /// <param name="extents">
        ///     The declared register-map extents. Client requests outside the declared extents are answered with
        ///     an IllegalDataAddress Modbus exception.
        /// </param>
        /// <remarks>
        ///     The server accepts requests for any unit identifier and echoes the request's unit identifier in the
        ///     response (the endpoint behavior the Modbus TCP specification intends for directly connected servers).
        ///     Bind failures propagate to the caller.
        /// </remarks>
        void Start(IPAddress listenAddress, int port, ModbusServerAreaExtents extents);

        /// <summary>
        ///     Stops listening. Buffer contents are retained.
        /// </summary>
        /// <remarks>
        ///     Benign teardown races of the underlying server library are caught and logged, never thrown.
        /// </remarks>
        void Stop();

        /// <summary>
        ///     Gets the holding register buffer in wire order. Hold <see cref="Lock" /> while accessing.
        /// </summary>
        /// <returns>The holding register buffer (2 bytes per register).</returns>
        Span<byte> GetHoldingRegisterBuffer();

        /// <summary>
        ///     Gets the input register buffer in wire order. Hold <see cref="Lock" /> while accessing.
        /// </summary>
        /// <returns>The input register buffer (2 bytes per register).</returns>
        Span<byte> GetInputRegisterBuffer();

        /// <summary>
        ///     Gets the bit-packed coil buffer. Hold <see cref="Lock" /> while accessing.
        /// </summary>
        /// <returns>The coil buffer (8 coils per byte, least significant bit first).</returns>
        Span<byte> GetCoilBuffer();

        /// <summary>
        ///     Gets the bit-packed discrete input buffer. Hold <see cref="Lock" /> while accessing.
        /// </summary>
        /// <returns>The discrete input buffer (8 inputs per byte, least significant bit first).</returns>
        Span<byte> GetDiscreteInputBuffer();
    }
}