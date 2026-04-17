using System;
using System.Threading;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <summary>
    ///     Provides non-blocking Modbus TCP client functionality for logic blocks.
    /// </summary>
    [PublicApi]

    /// <remarks>
    ///     <para>
    ///         The TCP connection is established lazily when the first read or write operation is executed and is maintained for subsequent operations.
    ///     </para>
    ///     <para>
    ///         The client uses a default port of 502 (standard Modbus TCP port) and a connection timeout of 3 seconds.
    ///         These can be changed using <see cref="Port" /> and <see cref="ConnectionTimeout" /> respectively.
    ///         The IP address must be set using <see cref="IpAddress" /> before any read or write operations can be performed.
    ///     </para>
    ///     <para>
    ///         All read and write operations are enqueued to an internal request queue and processed sequentially one at a time.
    ///         This sequential processing is required because the underlying Modbus TCP client library does not support concurrent operations on a single TCP connection.
    ///         The logic block itself is not blocked while operations are dequeued or executing - all operations are non-blocking with results delivered via callbacks.
    ///     </para>
    ///     <para>
    ///         The request queue is created the first time the client is enabled via <see cref="IsEnabled" />.
    ///         Queue settings (<see cref="QueueCapacity" /> and <see cref="QueueOverflowPolicy" />) must be configured before enabling the client for the first time.
    ///         Once the queue is created, these settings cannot be changed.
    ///     </para>
    ///     <para>
    ///         The client should be disposed when no longer needed to properly close the underlying TCP connection and release associated resources.
    ///         When the client is disposed, the internal request queue is closed and no new requests will be accepted.
    ///         Any read or write operations invoked after disposal will be rejected.
    ///         If an error callback is specified, it will be invoked with a <see cref="RequestDroppedException" />.
    ///         All enqueued requests and any currently executing request are canceled.
    ///         If an error callback is specified for these operations, it will be invoked with an <see cref="OperationCanceledException" />.
    ///     </para>
    ///     <para>
    ///         For scenarios requiring concurrent operations, additional client instances can be created via <see cref="ILogicBlockModbusTcpClientFactory.Create" />.
    ///         Each client instance maintains its own TCP connection and request queue, enabling parallel communication with the same or different Modbus servers.
    ///     </para>
    ///     <para>
    ///         The client is initially disabled (<see cref="IsEnabled" /> is <c>false</c>).
    ///         When disabled, all read and write operations are skipped.
    ///     </para>
    ///     <para>
    ///         For common exceptions that may be passed to error callbacks, see the documentation for <see cref="IModbusTcpClientWrapper" />.
    ///     </para>
    /// </remarks>
    public interface ILogicBlockModbusTcpClient : IDisposable
    {
        #region Client

        /// <summary>
        ///     Gets or sets whether the client is enabled. Default is <c>false</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         When set to <c>false</c>, all read and write operations become no-ops and will not execute.
        ///     </para>
        ///     <para>
        ///         This property serves two primary purposes:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 Temporarily disabling Modbus communication without requiring conditional logic in the logic block to prevent operation invocations.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 Preventing operations from executing with incomplete configuration. For example, initially the IP address will not be set.
        ///                 Without the client being disabled, any read or write operations would attempt to execute and fail due to missing configuration, flooding error logs.
        ///                 Similarly, when updating multiple configuration settings (such as both IP address and port), disabling the client first,
        ///                 updating all settings, then re-enabling it ensures operations only execute with the complete updated configuration.
        ///                 Disabling the client does not affect requests that have already been enqueued - those will continue to execute.
        ///             </description>
        ///         </item>
        ///     </list>
        /// </remarks>
        bool IsEnabled { get; set; }

        #endregion

        #region Queue

        /// <summary>
        ///     Gets or sets the maximum number of requests that can be queued. Default is 100.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This property must be set before enabling the client for the first time via <see cref="IsEnabled" />.
        ///         Once the request queue is created (when the client is first enabled), this setting cannot be changed.
        ///     </para>
        ///     <para>
        ///         When the queue reaches capacity, the behavior is determined by <see cref="QueueOverflowPolicy" />.
        ///     </para>
        /// </remarks>
        int QueueCapacity { get; set; }

        /// <summary>
        ///     Gets or sets the policy for handling new requests when the queue is full. Default is <see cref="QueueOverflowPolicy.DropOldest" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This property must be set before enabling the client for the first time via <see cref="IsEnabled" />.
        ///         Once the request queue is created (when the client is first enabled), this setting cannot be changed.
        ///     </para>
        ///     <para>
        ///         When the queue is full, a request will be dropped based on the policy.
        ///         If an error callback is specified for the dropped request, it will be invoked with a <see cref="RequestDroppedException" />.
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
        ///     Changes to this property do not trigger an immediate reconnect. The new port will be used when the next read or write operation is executed.
        /// </remarks>
        int Port { get; set; }

        /// <summary>
        ///     Gets or sets the IP address of the Modbus TCP server.
        /// </summary>
        /// <exception cref="FormatException">
        ///     Thrown when the IP address is null, empty, consists only of whitespace, or is not a valid IP address.
        /// </exception>
        /// <remarks>
        ///     Changes to this property do not trigger an immediate reconnect. The new IP address will be used when the next read or write operation is executed.
        /// </remarks>
        string? IpAddress { get; set; }

        /// <summary>
        ///     Manually disconnects from the Modbus TCP server.
        /// </summary>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
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
        /// </remarks>
        void Disconnect(IActorDispatcher dispatcher, Action? successCallback = null, Action<Exception>? errorCallback = null);

        #endregion

        #region ModbusDataAccess

        /// <summary>
        ///     Gets or sets the default timeout for Modbus operations. Default is 1 second.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This timeout is used when <c>operationTimeout</c> is not specified in individual operations.
        ///     </para>
        ///     <para>
        ///         The timeout measures only the network communication with the Modbus server: from when the request is sent to when the complete response is received.
        ///         It does not include time spent waiting in the queue, establishing a connection, parameter validation, or data conversion.
        ///     </para>
        ///     <para>
        ///         Changing this property does not affect operations already queued for execution. The new timeout
        ///         applies only to operations invoked after the change that do not specify their own <c>operationTimeout</c>.
        ///     </para>
        /// </remarks>
        TimeSpan DefaultOperationTimeout { get; set; }

        #region DiscreteInputs

        /// <summary>
        ///     Reads discrete inputs from a Modbus device (Function Code 2).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadDiscreteInputsAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadDiscreteInputs(int unitIdentifier,
                                ushort startingAddress,
                                ushort quantity,
                                IActorDispatcher dispatcher,
                                Action<bool[]> successCallback,
                                Action<Exception>? errorCallback = null,
                                TimeSpan? operationTimeout = null);

        #endregion

        #region Coils

        /// <summary>
        ///     Reads coils from a Modbus device (Function Code 1).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadCoilsAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadCoils(int unitIdentifier,
                       ushort startingAddress,
                       ushort quantity,
                       IActorDispatcher dispatcher,
                       Action<bool[]> successCallback,
                       Action<Exception>? errorCallback = null,
                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes a single coil to a Modbus device (Function Code 5).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the coil to write.</param>
        /// <param name="value">The value to write to the coil.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteSingleCoilAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteSingleCoil(int unitIdentifier,
                             ushort registerAddress,
                             bool value,
                             IActorDispatcher dispatcher,
                             Action? successCallback = null,
                             Action<Exception>? errorCallback = null,
                             TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple coils to a Modbus device (Function Code 15).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the coils.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleCoilsAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleCoils(int unitIdentifier,
                                ushort startingAddress,
                                bool[] values,
                                IActorDispatcher dispatcher,
                                Action? successCallback = null,
                                Action<Exception>? errorCallback = null,
                                TimeSpan? operationTimeout = null);

        #endregion

        #region InputRegisters

        /// <summary>
        ///     Reads input registers as raw bytes from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersRawAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful for debugging to inspect raw bytes received from the device,
        ///     or when the data format is not covered by the typed methods (e.g., custom data structures or non-standard encodings).
        ///     For standard data types, prefer the typed methods like <see cref="ReadInputRegistersAsShort" />, <see cref="ReadInputRegistersAsInt" />, etc.
        /// </remarks>
        void ReadInputRegistersRaw(int unitIdentifier,
                                   ushort startingAddress,
                                   ushort quantity,
                                   IActorDispatcher dispatcher,
                                   Action<byte[]> successCallback,
                                   Action<Exception>? errorCallback = null,
                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 16-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsShort(int unitIdentifier,
                                       ushort startingAddress,
                                       ushort quantity,
                                       IActorDispatcher dispatcher,
                                       Action<short[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 16-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsUShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsUShort(int unitIdentifier,
                                        ushort startingAddress,
                                        ushort quantity,
                                        IActorDispatcher dispatcher,
                                        Action<ushort[]> successCallback,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 32-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsInt(int unitIdentifier,
                                     ushort startingAddress,
                                     uint count,
                                     IActorDispatcher dispatcher,
                                     Action<int[]> successCallback,
                                     Action<Exception>? errorCallback = null,
                                     ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                     WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                     TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 32-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsUIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsUInt(int unitIdentifier,
                                      ushort startingAddress,
                                      uint count,
                                      IActorDispatcher dispatcher,
                                      Action<uint[]> successCallback,
                                      Action<Exception>? errorCallback = null,
                                      ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                      WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                      TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as 32-bit floating-point numbers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit floating-point numbers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsFloatAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsFloat(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<float[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 64-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsLongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsLong(int unitIdentifier,
                                      ushort startingAddress,
                                      uint count,
                                      IActorDispatcher dispatcher,
                                      Action<long[]> successCallback,
                                      Action<Exception>? errorCallback = null,
                                      ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                      WordOrder64 wordOrder = WordOrder64.ABCD,
                                      TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 64-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsULongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsULong(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<ulong[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder64 wordOrder = WordOrder64.ABCD,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as 64-bit floating-point numbers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit floating-point numbers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsDoubleAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadInputRegistersAsDouble(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<double[]> successCallback,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as a string from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadInputRegistersAsStringAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">The text encoding to use for decoding the string. Default is <see cref="TextEncoding.Ascii" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         String data is read sequentially from registers without byte or word order conversion.
        ///         The text encoding determines how the raw bytes are interpreted as characters.
        ///     </para>
        ///     <para>
        ///         Byte and word order parameters are not provided because:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 <b>UTF-8:</b> The byte sequence is fixed by the UTF-8 specification and cannot be reordered.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word ordering.
        ///                 The encoding type specified via <paramref name="textEncoding" /> handles this.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>ASCII:</b> While byte/word ordering could theoretically affect ASCII strings, standard Modbus
        ///                 convention stores ASCII sequentially (high byte = first character, low byte = second character).
        ///             </description>
        ///         </item>
        ///     </list>
        ///     <para>
        ///         If a device uses non-standard byte/word ordering for strings, use <see cref="ReadInputRegistersRaw" /> to read the data and manually reorder before decoding.
        ///     </para>
        /// </remarks>
        void ReadInputRegistersAsString(int unitIdentifier,
                                        ushort startingAddress,
                                        ushort quantity,
                                        IActorDispatcher dispatcher,
                                        Action<string> successCallback,
                                        Action<Exception>? errorCallback = null,
                                        TextEncoding textEncoding = TextEncoding.Ascii,
                                        TimeSpan? operationTimeout = null);

        #endregion

        #region HoldingRegisters

        /// <summary>
        ///     Reads holding registers as raw bytes from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersRawAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful for debugging to inspect raw bytes received from the device,
        ///     or when the data format is not covered by the typed methods (e.g., custom data structures or non-standard encodings).
        ///     For standard data types, prefer the typed methods like <see cref="ReadHoldingRegistersAsShort" />, <see cref="ReadHoldingRegistersAsInt" />, etc.
        /// </remarks>
        void ReadHoldingRegistersRaw(int unitIdentifier,
                                     ushort startingAddress,
                                     ushort quantity,
                                     IActorDispatcher dispatcher,
                                     Action<byte[]> successCallback,
                                     Action<Exception>? errorCallback = null,
                                     TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 16-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsShort(int unitIdentifier,
                                         ushort startingAddress,
                                         ushort quantity,
                                         IActorDispatcher dispatcher,
                                         Action<short[]> successCallback,
                                         Action<Exception>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 16-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsUShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsUShort(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          IActorDispatcher dispatcher,
                                          Action<ushort[]> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 32-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsInt(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<int[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 32-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsUIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsUInt(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<uint[]> successCallback,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as 32-bit floating-point numbers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit floating-point numbers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsFloatAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsFloat(int unitIdentifier,
                                         ushort startingAddress,
                                         uint count,
                                         IActorDispatcher dispatcher,
                                         Action<float[]> successCallback,
                                         Action<Exception>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 64-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsLongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsLong(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<long[]> successCallback,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 64-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsULongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsULong(int unitIdentifier,
                                         ushort startingAddress,
                                         uint count,
                                         IActorDispatcher dispatcher,
                                         Action<ulong[]> successCallback,
                                         Action<Exception>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         WordOrder64 wordOrder = WordOrder64.ABCD,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as 64-bit floating-point numbers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit floating-point numbers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsDoubleAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order the data is received in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is received in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void ReadHoldingRegistersAsDouble(int unitIdentifier,
                                          ushort startingAddress,
                                          uint count,
                                          IActorDispatcher dispatcher,
                                          Action<double[]> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                          WordOrder64 wordOrder = WordOrder64.ABCD,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as a string from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.ReadHoldingRegistersAsStringAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">The text encoding to use for decoding the string. Default is <see cref="TextEncoding.Ascii" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         String data is read sequentially from registers without byte or word order conversion.
        ///         The text encoding determines how the raw bytes are interpreted as characters.
        ///     </para>
        ///     <para>
        ///         Byte and word order parameters are not provided because:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 <b>UTF-8:</b> The byte sequence is fixed by the UTF-8 specification and cannot be reordered.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word ordering.
        ///                 The encoding type specified via <paramref name="textEncoding" /> handles this.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>ASCII:</b> While byte/word ordering could theoretically affect ASCII strings, standard Modbus
        ///                 convention stores ASCII sequentially (high byte = first character, low byte = second character).
        ///             </description>
        ///         </item>
        ///     </list>
        ///     <para>
        ///         If a device uses non-standard byte/word ordering for strings, use <see cref="ReadHoldingRegistersRaw" /> to read the data and manually reorder before decoding.
        ///     </para>
        /// </remarks>
        void ReadHoldingRegistersAsString(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          IActorDispatcher dispatcher,
                                          Action<string> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          TextEncoding textEncoding = TextEncoding.Ascii,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes a single holding register as a signed 16-bit integer to a Modbus device (Function Code 6).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the register to write.</param>
        /// <param name="value">The value to write to the register.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see
        ///     <see cref="IModbusTcpClientWrapper.WriteSingleHoldingRegisterAsync(int, ushort, short, ByteOrder, TimeSpan, CancellationToken)" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteSingleHoldingRegister(int unitIdentifier,
                                        ushort registerAddress,
                                        short value,
                                        IActorDispatcher dispatcher,
                                        Action? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes a single holding register as an unsigned 16-bit integer to a Modbus device (Function Code 6).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the register to write.</param>
        /// <param name="value">The value to write to the register.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see
        ///     <see cref="IModbusTcpClientWrapper.WriteSingleHoldingRegisterAsync(int, ushort, ushort, ByteOrder, TimeSpan, CancellationToken)" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteSingleHoldingRegister(int unitIdentifier,
                                        ushort registerAddress,
                                        ushort value,
                                        IActorDispatcher dispatcher,
                                        Action? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as raw bytes to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The raw byte values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersRawAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful when the data format is not covered by the typed methods (e.g., custom data structures or non-standard encodings).
        ///     For standard data types, prefer the typed methods like <see cref="WriteMultipleHoldingRegistersAsShort" />, <see cref="WriteMultipleHoldingRegistersAsInt" />, etc.
        /// </remarks>
        void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                              ushort startingAddress,
                                              byte[] values,
                                              IActorDispatcher dispatcher,
                                              Action? successCallback = null,
                                              Action<Exception>? errorCallback = null,
                                              TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as signed 16-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsShort(int unitIdentifier,
                                                  ushort startingAddress,
                                                  short[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action? successCallback = null,
                                                  Action<Exception>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 16-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsUShortAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsUShort(int unitIdentifier,
                                                   ushort startingAddress,
                                                   ushort[] values,
                                                   IActorDispatcher dispatcher,
                                                   Action? successCallback = null,
                                                   Action<Exception>? errorCallback = null,
                                                   ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as signed 32-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsInt(int unitIdentifier,
                                                ushort startingAddress,
                                                int[] values,
                                                IActorDispatcher dispatcher,
                                                Action? successCallback = null,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 32-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsUIntAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsUInt(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint[] values,
                                                 IActorDispatcher dispatcher,
                                                 Action? successCallback = null,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                 TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as 32-bit floating-point numbers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsFloatAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsFloat(int unitIdentifier,
                                                  ushort startingAddress,
                                                  float[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action? successCallback = null,
                                                  Action<Exception>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as signed 64-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsLongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsLong(int unitIdentifier,
                                                 ushort startingAddress,
                                                 long[] values,
                                                 IActorDispatcher dispatcher,
                                                 Action? successCallback = null,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder64 wordOrder = WordOrder64.ABCD,
                                                 TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 64-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsULongAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsULong(int unitIdentifier,
                                                  ushort startingAddress,
                                                  ulong[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action? successCallback = null,
                                                  Action<Exception>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  WordOrder64 wordOrder = WordOrder64.ABCD,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as 64-bit floating-point numbers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsDoubleAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="byteOrder">The byte order to write the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to write the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        void WriteMultipleHoldingRegistersAsDouble(int unitIdentifier,
                                                   ushort startingAddress,
                                                   double[] values,
                                                   IActorDispatcher dispatcher,
                                                   Action? successCallback = null,
                                                   Action<Exception>? errorCallback = null,
                                                   ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                   WordOrder64 wordOrder = WordOrder64.ABCD,
                                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as a string to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="value">The string value to write to the registers.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see <see cref="IModbusTcpClientWrapper.WriteMultipleHoldingRegistersAsStringAsync" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">The text encoding to use for encoding the string. Default is <see cref="TextEncoding.Ascii" />.</param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         If the encoded string has an odd byte length, a null byte (0x00) is appended at the end
        ///         to ensure alignment to Modbus register boundaries (2 bytes per register).
        ///     </para>
        ///     <para>
        ///         String data is written sequentially to registers without byte or word order conversion.
        ///         The text encoding determines how the string characters are converted to raw bytes.
        ///     </para>
        ///     <para>
        ///         Byte and word order parameters are not provided because:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>
        ///                 <b>UTF-8:</b> The byte sequence is fixed by the UTF-8 specification and cannot be reordered.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word ordering.
        ///                 The encoding type specified via <paramref name="textEncoding" /> handles this.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 <b>ASCII:</b> While byte/word ordering could theoretically affect ASCII strings, standard Modbus
        ///                 convention stores ASCII sequentially (high byte = first character, low byte = second character).
        ///             </description>
        ///         </item>
        ///     </list>
        ///     <para>
        ///         If a device requires non-standard byte/word ordering for strings, manually encode the string,
        ///         reorder the bytes as needed, and use <see cref="WriteMultipleHoldingRegistersRaw" />.
        ///     </para>
        /// </remarks>
        void WriteMultipleHoldingRegistersAsString(int unitIdentifier,
                                                   ushort startingAddress,
                                                   string value,
                                                   IActorDispatcher dispatcher,
                                                   Action? successCallback = null,
                                                   Action<Exception>? errorCallback = null,
                                                   TextEncoding textEncoding = TextEncoding.Ascii,
                                                   TimeSpan? operationTimeout = null);

        #endregion

        #endregion
    }
}