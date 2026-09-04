using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Core.Client
{
    /// <summary>
    ///     The Modbus operations and link diagnostics every Dale Modbus client offers, whatever the transport.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every read and write is non-blocking: the call returns immediately and the result arrives later on the
    ///         logic block's own thread, through the dispatcher passed with the call. Pass <c>this</c> from inside a
    ///         logic block.
    ///     </para>
    ///     <para>
    ///         Both callbacks carry a <see cref="ModbusReceipt" /> — when the response was observed, how long it took on
    ///         the wire, how long it waited before that, and how it ended. The instants are taken before the callback is
    ///         handed to the block's mailbox, so they measure the transaction and not the block's own backlog. Ignore it
    ///         with a discard where you do not need it: <c>(values, _) =&gt; Power = values[0]</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="Link" /> is the same information accumulated per client: the current verdict on the device, the
    ///         last contact, and counts and latencies per outcome. It is a snapshot struct, readable at any time and
    ///         publishable as a single <c>[ServiceProperty]</c>. A locally decided outcome — a full or backed-up queue,
    ///         a bad unit id — never moves <c>Link.State</c>, so a congested client is distinguishable from a broken
    ///         device. Which of them carry a lifetime counter is stated on <see cref="ModbusLinkSummary" />.
    ///     </para>
    ///     <para>
    ///         <see cref="MaxQueuedAge" /> bounds how stale a request may be when its turn finally comes. Past that age
    ///         a request completes immediately with <c>Outcome.Expired</c> and the device is never contacted, so a queue
    ///         that built up during an outage drains in microseconds instead of replaying minutes of dead work. It
    ///         defaults to 30 seconds; set it to <c>null</c> to let every queued request through however old it is.
    ///     </para>
    ///     <para>
    ///         <see cref="DefaultOperationTimeout" /> covers the wire only — from dispatch to the complete response. The
    ///         wait before dispatch is <see cref="MaxQueuedAge" />'s business, and both appear separately on the receipt
    ///         as <c>RoundTrip</c> and <c>QueuedWait</c>.
    ///     </para>
    ///     <para>
    ///         What differs between the transports is inherent and is stated on each client type: Modbus TCP owns one
    ///         socket and one request queue per client, so it also reports a connection summary and a queue depth, and
    ///         defaults to a 1 second operation timeout. Modbus RTU goes through one runtime-wide handler shared by every
    ///         binding, so its overflow limit and its 1 second expiry sweep are shared with other blocks, its queue depth
    ///         is reported as zero, and it defaults to a 5 second operation timeout.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface IModbusClient
    {
        /// <summary>
        ///     Gets or sets whether the client executes operations. Default is <c>false</c>.
        /// </summary>
        /// <remarks>
        ///     While disabled every read and write is a no-op, so a block can be configured field by field without its
        ///     operations failing on half-set configuration. Requests already accepted still run.
        /// </remarks>
        bool IsEnabled { get; set; }

        /// <summary>
        ///     Gets or sets the wire timeout used by operations that do not pass their own.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to zero or a negative duration.</exception>
        /// <remarks>
        ///     It measures the network exchange with the device only — from dispatch to the complete response — not the
        ///     wait before dispatch, parameter validation or data conversion. Changing it does not affect operations
        ///     already accepted.
        /// </remarks>
        TimeSpan DefaultOperationTimeout { get; set; }

        /// <summary>
        ///     Gets or sets how stale a request may be when its turn comes. Default is 30 seconds; <c>null</c> disables the check.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to zero or a negative duration.</exception>
        /// <remarks>
        ///     Checked immediately before dispatch, so a value set now applies to requests already waiting. An expired
        ///     request completes through its error callback with a <c>RequestExpiredException</c> and
        ///     <c>Outcome.Expired</c>, and the device is not contacted.
        /// </remarks>
        TimeSpan? MaxQueuedAge { get; set; }

        /// <summary>
        ///     Gets a snapshot of the link to the device: current verdict, last contact, and counts per outcome.
        /// </summary>
        ModbusLinkSummary Link { get; }

        /// <summary>
        ///     Reads discrete inputs (Function Code 2).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the bits read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadDiscreteInputs(int unitIdentifier,
                                ushort startingAddress,
                                ushort quantity,
                                IActorDispatcher dispatcher,
                                Action<bool[], ModbusReceipt> successCallback,
                                Action<Exception, ModbusReceipt>? errorCallback = null,
                                TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads coils (Function Code 1).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the bits read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadCoils(int unitIdentifier,
                       ushort startingAddress,
                       ushort quantity,
                       IActorDispatcher dispatcher,
                       Action<bool[], ModbusReceipt> successCallback,
                       Action<Exception, ModbusReceipt>? errorCallback = null,
                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes one coil (Function Code 5).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="registerAddress">The address of the register or coil to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteSingleCoil(int unitIdentifier,
                             ushort registerAddress,
                             bool value,
                             IActorDispatcher dispatcher,
                             Action<ModbusReceipt>? successCallback = null,
                             Action<Exception, ModbusReceipt>? errorCallback = null,
                             TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes several consecutive coils (Function Code 15).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleCoils(int unitIdentifier,
                                ushort startingAddress,
                                bool[] values,
                                IActorDispatcher dispatcher,
                                Action<ModbusReceipt>? successCallback = null,
                                Action<Exception, ModbusReceipt>? errorCallback = null,
                                TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as raw bytes (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the raw response bytes and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersRaw(int unitIdentifier,
                                   ushort startingAddress,
                                   ushort quantity,
                                   IActorDispatcher dispatcher,
                                   Action<byte[], ModbusReceipt> successCallback,
                                   Action<Exception, ModbusReceipt>? errorCallback = null,
                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 16-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsShort(int unitIdentifier,
                                       ushort startingAddress,
                                       ushort quantity,
                                       IActorDispatcher dispatcher,
                                       Action<short[], ModbusReceipt> successCallback,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 16-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsUShort(int unitIdentifier,
                                        ushort startingAddress,
                                        ushort quantity,
                                        IActorDispatcher dispatcher,
                                        Action<ushort[], ModbusReceipt> successCallback,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 32-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsInt(int unitIdentifier,
                                     ushort startingAddress,
                                     uint count,
                                     IActorDispatcher dispatcher,
                                     Action<int[], ModbusReceipt> successCallback,
                                     Action<Exception, ModbusReceipt>? errorCallback = null,
                                     ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                     WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                     TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 32-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsUInt(int unitIdentifier,
                                      ushort startingAddress,
                                      uint count,
                                      IActorDispatcher dispatcher,
                                      Action<uint[], ModbusReceipt> successCallback,
                                      Action<Exception, ModbusReceipt>? errorCallback = null,
                                      ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                      WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                      TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as 32-bit floating point values (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsFloat(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<float[], ModbusReceipt> successCallback,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 64-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsLong(int unitIdentifier,
                                      ushort startingAddress,
                                      uint count,
                                      IActorDispatcher dispatcher,
                                      Action<long[], ModbusReceipt> successCallback,
                                      Action<Exception, ModbusReceipt>? errorCallback = null,
                                      ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                      WordOrder64 wordOrder = WordOrder64.ABCD,
                                      TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as unsigned 64-bit integers (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsULong(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<ulong[], ModbusReceipt> successCallback,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder64 wordOrder = WordOrder64.ABCD,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as 64-bit floating point values (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsDouble(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<double[], ModbusReceipt> successCallback,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as text (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the decoded text and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="textEncoding">The character encoding of the register bytes.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadInputRegistersAsString(int unitIdentifier,
                                        ushort startingAddress,
                                        ushort quantity,
                                        IActorDispatcher dispatcher,
                                        Action<string, ModbusReceipt> successCallback,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        TextEncoding textEncoding = TextEncoding.Ascii,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as raw bytes (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the raw response bytes and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersRaw(int unitIdentifier,
                                     ushort startingAddress,
                                     ushort quantity,
                                     IActorDispatcher dispatcher,
                                     Action<byte[], ModbusReceipt> successCallback,
                                     Action<Exception, ModbusReceipt>? errorCallback = null,
                                     TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 16-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsShort(int unitIdentifier,
                                         ushort startingAddress,
                                         ushort quantity,
                                         IActorDispatcher dispatcher,
                                         Action<short[], ModbusReceipt> successCallback,
                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 16-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsUShort(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          IActorDispatcher dispatcher,
                                          Action<ushort[], ModbusReceipt> successCallback,
                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 32-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsInt(int unitIdentifier,
                                       ushort startingAddress,
                                       uint count,
                                       IActorDispatcher dispatcher,
                                       Action<int[], ModbusReceipt> successCallback,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 32-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsUInt(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<uint[], ModbusReceipt> successCallback,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as 32-bit floating point values (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsFloat(int unitIdentifier,
                                         ushort startingAddress,
                                         uint count,
                                         IActorDispatcher dispatcher,
                                         Action<float[], ModbusReceipt> successCallback,
                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 64-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsLong(int unitIdentifier,
                                        ushort startingAddress,
                                        uint count,
                                        IActorDispatcher dispatcher,
                                        Action<long[], ModbusReceipt> successCallback,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as unsigned 64-bit integers (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsULong(int unitIdentifier,
                                         ushort startingAddress,
                                         uint count,
                                         IActorDispatcher dispatcher,
                                         Action<ulong[], ModbusReceipt> successCallback,
                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                         WordOrder64 wordOrder = WordOrder64.ABCD,
                                         TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as 64-bit floating point values (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="count">The number of values to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the values read and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsDouble(int unitIdentifier,
                                          ushort startingAddress,
                                          uint count,
                                          IActorDispatcher dispatcher,
                                          Action<double[], ModbusReceipt> successCallback,
                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                          WordOrder64 wordOrder = WordOrder64.ABCD,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as text (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or bit to read.</param>
        /// <param name="quantity">The number of registers or bits to read.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the decoded text and the transaction's receipt.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="textEncoding">The character encoding of the register bytes.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void ReadHoldingRegistersAsString(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          IActorDispatcher dispatcher,
                                          Action<string, ModbusReceipt> successCallback,
                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                          TextEncoding textEncoding = TextEncoding.Ascii,
                                          TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes one holding register (Function Code 6).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="registerAddress">The address of the register or coil to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteSingleHoldingRegister(int unitIdentifier,
                                        ushort registerAddress,
                                        short value,
                                        IActorDispatcher dispatcher,
                                        Action<ModbusReceipt>? successCallback = null,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes one holding register (Function Code 6).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="registerAddress">The address of the register or coil to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteSingleHoldingRegister(int unitIdentifier,
                                        ushort registerAddress,
                                        ushort value,
                                        IActorDispatcher dispatcher,
                                        Action<ModbusReceipt>? successCallback = null,
                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                        TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers from raw bytes (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                              ushort startingAddress,
                                              byte[] values,
                                              IActorDispatcher dispatcher,
                                              Action<ModbusReceipt>? successCallback = null,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
                                              TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as signed 16-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsShort(int unitIdentifier,
                                                  ushort startingAddress,
                                                  short[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action<ModbusReceipt>? successCallback = null,
                                                  Action<Exception, ModbusReceipt>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as unsigned 16-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsUShort(int unitIdentifier,
                                                   ushort startingAddress,
                                                   ushort[] values,
                                                   IActorDispatcher dispatcher,
                                                   Action<ModbusReceipt>? successCallback = null,
                                                   Action<Exception, ModbusReceipt>? errorCallback = null,
                                                   ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as signed 32-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsInt(int unitIdentifier,
                                                ushort startingAddress,
                                                int[] values,
                                                IActorDispatcher dispatcher,
                                                Action<ModbusReceipt>? successCallback = null,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as unsigned 32-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsUInt(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint[] values,
                                                 IActorDispatcher dispatcher,
                                                 Action<ModbusReceipt>? successCallback = null,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                 TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as 32-bit floating point values (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsFloat(int unitIdentifier,
                                                  ushort startingAddress,
                                                  float[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action<ModbusReceipt>? successCallback = null,
                                                  Action<Exception, ModbusReceipt>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as signed 64-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsLong(int unitIdentifier,
                                                 ushort startingAddress,
                                                 long[] values,
                                                 IActorDispatcher dispatcher,
                                                 Action<ModbusReceipt>? successCallback = null,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder64 wordOrder = WordOrder64.ABCD,
                                                 TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as unsigned 64-bit integers (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsULong(int unitIdentifier,
                                                  ushort startingAddress,
                                                  ulong[] values,
                                                  IActorDispatcher dispatcher,
                                                  Action<ModbusReceipt>? successCallback = null,
                                                  Action<Exception, ModbusReceipt>? errorCallback = null,
                                                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                  WordOrder64 wordOrder = WordOrder64.ABCD,
                                                  TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as 64-bit floating point values (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="byteOrder">The byte order of each register.</param>
        /// <param name="wordOrder">The order of the registers making up one value.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsDouble(int unitIdentifier,
                                                   ushort startingAddress,
                                                   double[] values,
                                                   IActorDispatcher dispatcher,
                                                   Action<ModbusReceipt>? successCallback = null,
                                                   Action<Exception, ModbusReceipt>? errorCallback = null,
                                                   ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                   WordOrder64 wordOrder = WordOrder64.ABCD,
                                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes consecutive holding registers as text (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the device.</param>
        /// <param name="startingAddress">The address of the first register or coil to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="dispatcher">The dispatcher that invokes the callbacks — pass the calling logic block (<c>this</c>).</param>
        /// <param name="successCallback">Invoked with the transaction's receipt when the device acknowledges the write.</param>
        /// <param name="errorCallback">
        ///     Invoked with the failure and the transaction's receipt. Failures are logged whether or not
        ///     this is set.
        /// </param>
        /// <param name="textEncoding">The character encoding of the register bytes.</param>
        /// <param name="operationTimeout">
        ///     The time allowed on the wire before the operation is abandoned. <c>null</c> uses
        ///     <see cref="DefaultOperationTimeout" />.
        /// </param>
        void WriteMultipleHoldingRegistersAsString(int unitIdentifier,
                                                   ushort startingAddress,
                                                   string value,
                                                   IActorDispatcher dispatcher,
                                                   Action<ModbusReceipt>? successCallback = null,
                                                   Action<Exception, ModbusReceipt>? errorCallback = null,
                                                   TextEncoding textEncoding = TextEncoding.Ascii,
                                                   TimeSpan? operationTimeout = null);
    }
}