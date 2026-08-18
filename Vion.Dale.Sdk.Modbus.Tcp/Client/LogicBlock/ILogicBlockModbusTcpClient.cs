using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Client;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <summary>
    ///     Provides non-blocking Modbus TCP client functionality for logic blocks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reads, writes and diagnostics are those of <see cref="IModbusClient" />; this type adds what is
    ///         specific to owning a socket and a request queue.
    ///     </para>
    ///     <para>
    ///         The TCP connection is established lazily inside the first operation that needs one and is kept for
    ///         subsequent operations. Set <see cref="IpAddress" /> before enabling the client;
    ///         <see cref="Port" /> defaults to 502 and <see cref="ConnectionTimeout" /> to 3 seconds. Because the
    ///         handshake happens inside an operation, that operation's <c>RoundTrip</c> includes it — see
    ///         <see cref="Connection" />, whose <c>LastConnectDuration</c> is how you separate the two.
    ///     </para>
    ///     <para>
    ///         All operations are enqueued and executed one at a time: the underlying Modbus TCP library cannot run
    ///         concurrent operations on one connection. The logic block is never blocked by this — results arrive
    ///         through callbacks. Use <see cref="ILogicBlockModbusTcpClientFactory.Create" /> for a second, independent
    ///         connection and queue.
    ///     </para>
    ///     <para>
    ///         The queue is created the first time the client is enabled via <c>IsEnabled</c>.
    ///         <see cref="QueueCapacity" /> and <see cref="QueueOverflowPolicy" /> must be set before that and cannot be
    ///         changed afterwards. <c>MaxQueuedAge</c>, in contrast, is read at dequeue and can be changed at any time.
    ///     </para>
    ///     <para>
    ///         Dispose the client when it is no longer needed. Disposal closes the connection and the queue: subsequent
    ///         operations are rejected with a <see cref="RequestDroppedException" /> whose <c>Reason</c> is
    ///         <see cref="RequestDropReason.ClientDisposed" />, and requests already queued or in flight are cancelled.
    ///     </para>
    ///     <para>
    ///         Link policy. A timeout, transport error or protocol error on an established connection closes the
    ///         socket, so the next operation reconnects: a peer that dropped and came back is reached again without
    ///         operator action, and a stray response cannot be read as the next transaction's answer. Only failed
    ///         connects drive the backoff — from the second consecutive one the client waits
    ///         <see cref="ConnectBackoff" /> (1 second), doubling per further failure up to
    ///         <see cref="ConnectBackoffMax" /> (30 seconds). Operations issued during that wait fail immediately with
    ///         a <see cref="LinkBackoffException" /> and an <c>Outcome.BackedOff</c> receipt, so the queue drains
    ///         instead of filling and the device is not contacted once per queued request. A successful connect, a
    ///         <em>changed</em> <see cref="IpAddress" /> or <see cref="Port" />, or re-enabling the client ends the
    ///         wait, so a corrected address takes effect on the very next operation. Setting an address or port to the
    ///         value already in force does nothing.
    ///     </para>
    ///     <para>
    ///         No operation is ever retried automatically. Reads are re-polled by construction, and repeating a write
    ///         after a fault is not safe to decide for the caller — a pulse would be written twice.
    ///     </para>
    ///     <para>
    ///         Compared with Modbus RTU: this client owns its socket and its queue, so
    ///         <see cref="QueuedRequestCount" /> is real, overflow is per client, and <see cref="Connection" /> exists.
    ///         Its default operation timeout is 1 second. RTU has no link policy: there is no connection to back off
    ///         from.
    ///     </para>
    ///     <para>
    ///         For the exceptions that reach an error callback, see the documentation for
    ///         <see cref="IModbusTcpClientWrapper" />.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface ILogicBlockModbusTcpClient : IModbusClient, IDisposable
    {
        #region Queue

        /// <summary>
        ///     Gets or sets the maximum number of requests that can be queued. Default is 256.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This property must be set before enabling the client for the first time via <c>IsEnabled</c>.
        ///         Once the request queue is created (when the client is first enabled), this setting cannot be changed.
        ///     </para>
        ///     <para>
        ///         When the queue reaches capacity, the behavior is determined by <see cref="QueueOverflowPolicy" />.
        ///     </para>
        /// </remarks>
        int QueueCapacity { get; set; }

        /// <summary>
        ///     Gets or sets the policy for handling new requests when the queue is full. Default is
        ///     <see cref="QueueOverflowPolicy.DropOldest" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This property must be set before enabling the client for the first time via <c>IsEnabled</c>.
        ///         Once the request queue is created (when the client is first enabled), this setting cannot be changed.
        ///     </para>
        ///     <para>
        ///         When the queue is full, a request will be dropped based on the policy.
        ///         If an error callback is specified for the dropped request, it will be invoked with a
        ///         <see cref="RequestDroppedException" /> and a receipt whose outcome is <c>Dropped</c>.
        ///     </para>
        /// </remarks>
        QueueOverflowPolicy QueueOverflowPolicy { get; set; }

        /// <summary>
        ///     Gets the current number of requests queued for execution.
        /// </summary>
        /// <remarks>
        ///     This count only includes requests waiting in the queue.
        ///     A request that is currently executing (in-flight) is not included in this count.
        /// </remarks>
        int QueuedRequestCount { get; }

        #endregion

        #region Connection

        /// <summary>
        ///     Gets or sets the timeout for connection attempts to the Modbus TCP server.
        /// </summary>
        /// <remarks>
        ///     Changes to this property do not take effect until the next connection attempt is made.
        /// </remarks>
        TimeSpan ConnectionTimeout { get; set; }

        /// <summary>
        ///     Gets or sets the port number used to connect to the Modbus TCP server.
        /// </summary>
        /// <exception cref="FormatException">
        ///     Thrown when the port number is outside the valid range (0-65535).
        /// </exception>
        /// <remarks>
        ///     Changes to this property do not trigger an immediate reconnect. The new port will be used when the next read or
        ///     write operation is executed, and ends any connect backoff. Setting the port already in force does nothing.
        /// </remarks>
        int Port { get; set; }

        /// <summary>
        ///     Gets or sets the IP address of the Modbus TCP server.
        /// </summary>
        /// <exception cref="FormatException">
        ///     Thrown when the IP address is null, empty, consists only of whitespace, or is not a valid IP address.
        /// </exception>
        /// <remarks>
        ///     Changes to this property do not trigger an immediate reconnect. The new IP address will be used when the next read
        ///     or write operation is executed, and ends any connect backoff — so a corrected address applies at once. Setting the
        ///     address already in force does nothing.
        /// </remarks>
        string? IpAddress { get; set; }

        /// <summary>
        ///     Gets or sets how long the client waits after the second consecutive failed connect. Default is 1 second.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown when the value is not greater than zero, or exceeds <see cref="ConnectBackoffMax" />.
        /// </exception>
        /// <remarks>
        ///     Can be changed at any time. The wait doubles per further consecutive failure up to
        ///     <see cref="ConnectBackoffMax" />; setting both to the same value gives a constant wait, and there is no
        ///     value that turns the backoff off.
        /// </remarks>
        TimeSpan ConnectBackoff { get; set; }

        /// <summary>
        ///     Gets or sets the longest the client waits between connection attempts. Default is 30 seconds.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown when the value is smaller than <see cref="ConnectBackoff" />.
        /// </exception>
        TimeSpan ConnectBackoffMax { get; set; }

        /// <summary>
        ///     Gets a snapshot of the socket: whether it is up, and how connection attempts have gone.
        /// </summary>
        ModbusTcpConnectionSummary Connection { get; }

        /// <summary>
        ///     Manually disconnects from the Modbus TCP server.
        /// </summary>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="successCallback">
        ///     The callback invoked when the operation succeeds.
        /// </param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         This method is useful for operations that execute rarely, where the overhead of establishing
        ///         a connection for each operation outweighs the benefit of keeping the connection open.
        ///     </para>
        ///     <para>
        ///         The connection will be automatically re-established on the next read or write operation.
        ///     </para>
        ///     <para>
        ///         This method is idempotent - calling it when already disconnected has no effect.
        ///     </para>
        ///     <para>
        ///         It is a control operation, not a device transaction: it carries no receipt, does not contribute to
        ///         <c>Link</c>, and is never expired by <c>MaxQueuedAge</c>.
        ///     </para>
        /// </remarks>
        void Disconnect(IActorDispatcher dispatcher, Action? successCallback = null, Action<Exception>? errorCallback = null);

        #endregion
    }
}