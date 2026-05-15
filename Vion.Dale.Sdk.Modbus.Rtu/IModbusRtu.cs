using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Provides Modbus RTU read and write operations.
    /// </summary>
    /// <remarks>
    ///     Initially disabled (<see cref="IsEnabled" /> is <c>false</c>).
    ///     When disabled, all operations are skipped.
    ///     <para>
    ///         All instances share a single <see cref="ModbusRtuHandler" />. Requests from all <see cref="IModbusRtu" />
    ///         instances are processed
    ///         sequentially in the order they are received, and all instances share a maximum pending request limit of
    ///         <see cref="ModbusRtuHandler.MaxPendingRequests" />.
    ///     </para>
    ///     <para>
    ///         The following exceptions may be passed to the error callback on any read or write operation:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>
    ///                     <see cref="InvalidUnitIdentifierException" /> — The specified unit identifier is less than 0 or
    ///                     greater than 255.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="PendingRequestsLimitReachedException" /> — There is a limit on how many requests can be
    ///                     pending at the same time.
    ///                     When this limit is reached, new requests are rejected immediately.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="HalElementMappingNotFoundException" /> — No HAL element mapping was found for the
    ///                     associated IO ID.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="OperationTimeoutException" /> — The operation did not complete within the specified
    ///                     timeout.
    ///                     Every second, pending requests are checked for expiration and immediately completed if expired.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="ModbusException" /> — An error was returned by the Modbus device.
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         The following exceptions apply only to specific operations:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>
    ///                     <see cref="InvalidBitQuantityException" /> — Fewer coils or discrete inputs were returned than were
    ///                     requested.
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="InvalidCountException" /> — The resulting register quantity exceeds the maximum of 65535
    ///                     (e.g., requesting 17000 64-bit values requires 68000 registers).
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description>
    ///                     <see cref="ModbusResponseAlignmentException" /> — The number of bytes received does not match the
    ///                     expected amount for the requested registers.
    ///                     For example, reading 2 registers expects 4 bytes; if 5 bytes are returned, this exception is
    ///                     thrown.
    ///                 </description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("ModbusRtu")]
    public interface IModbusRtu
    {
        #region Client

        /// <summary>
        ///     Gets or sets a value indicating whether operations are enabled.
        /// </summary>
        bool IsEnabled { get; set; }

        #endregion

        #region ModbusDataAccess

        /// <summary>
        ///     Gets or sets the default timeout for Modbus operations. Default is 5 seconds.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This timeout is used when <c>operationTimeout</c> is not specified in individual operations.
        ///     </para>
        ///     <para>
        ///         The timeout starts when the operation is invoked. Expiration is checked periodically (approximately every
        ///         second),
        ///         so the actual time before an expired operation is completed may exceed the specified timeout.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
                       Action<bool[]> successCallback,
                       Action<Exception>? errorCallback = null,
                       TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes a single coil to a Modbus device (Function Code 5).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the coil to write.</param>
        /// <param name="value">The value to write to the coil.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
                             Action? successCallback = null,
                             Action<Exception>? errorCallback = null,
                             TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple coils to a Modbus device (Function Code 15).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the coils.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful for debugging to inspect raw bytes received from the device,
        ///     or when the data format is not covered by the typed methods (e.g., custom data structures or non-standard
        ///     encodings).
        ///     For standard data types, prefer the typed methods like <see cref="ReadInputRegistersAsShort" />,
        ///     <see cref="ReadInputRegistersAsInt" />, etc.
        /// </remarks>
        void ReadInputRegistersRaw(int unitIdentifier,
                                   ushort startingAddress,
                                   ushort quantity,
                                   Action<byte[]> successCallback,
                                   Action<Exception>? errorCallback = null,
                                   TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads input registers as signed 16-bit integers from a Modbus device (Function Code 4).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">
        ///     The text encoding to use for decoding the string. Default is
        ///     <see cref="TextEncoding.Ascii" />.
        /// </param>
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
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word
        ///                 ordering.
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
        ///         If a device uses non-standard byte/word ordering for strings, use <see cref="ReadInputRegistersRaw" /> to read
        ///         the data and manually reorder before decoding.
        ///     </para>
        /// </remarks>
        void ReadInputRegistersAsString(int unitIdentifier,
                                        ushort startingAddress,
                                        ushort quantity,
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful for debugging to inspect raw bytes received from the device,
        ///     or when the data format is not covered by the typed methods (e.g., custom data structures or non-standard
        ///     encodings).
        ///     For standard data types, prefer the typed methods like <see cref="ReadHoldingRegistersAsShort" />,
        ///     <see cref="ReadHoldingRegistersAsInt" />, etc.
        /// </remarks>
        void ReadHoldingRegistersRaw(int unitIdentifier,
                                     ushort startingAddress,
                                     ushort quantity,
                                     Action<byte[]> successCallback,
                                     Action<Exception>? errorCallback = null,
                                     TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Reads holding registers as signed 16-bit integers from a Modbus device (Function Code 3).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">
        ///     The text encoding to use for decoding the string. Default is
        ///     <see cref="TextEncoding.Ascii" />.
        /// </param>
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
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word
        ///                 ordering.
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
        ///         If a device uses non-standard byte/word ordering for strings, use <see cref="ReadHoldingRegistersRaw" /> to
        ///         read the data and manually reorder before decoding.
        ///     </para>
        /// </remarks>
        void ReadHoldingRegistersAsString(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="operationTimeout">
        ///     The maximum time allowed for the Modbus operation before it is canceled.
        ///     If <c>null</c>, <see cref="DefaultOperationTimeout" /> is used.
        ///     See <see cref="DefaultOperationTimeout" /> for details on what the timeout covers.
        /// </param>
        /// <remarks>
        ///     This method is useful when the data format is not covered by the typed methods (e.g., custom data structures or
        ///     non-standard encodings).
        ///     For standard data types, prefer the typed methods like <see cref="WriteMultipleHoldingRegistersAsShort" />,
        ///     <see cref="WriteMultipleHoldingRegistersAsInt" />, etc.
        /// </remarks>
        void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                              ushort startingAddress,
                                              byte[] values,
                                              Action? successCallback = null,
                                              Action<Exception>? errorCallback = null,
                                              TimeSpan? operationTimeout = null);

        /// <summary>
        ///     Writes multiple holding registers as signed 16-bit integers to a Modbus device (Function Code 16).
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the registers.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
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
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     For common exceptions that may be passed to this callback, see the remarks on <see cref="IModbusRtu" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="textEncoding">
        ///     The text encoding to use for encoding the string. Default is
        ///     <see cref="TextEncoding.Ascii" />.
        /// </param>
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
        ///                 <b>UTF-16:</b> Only endianness matters (big-endian vs little-endian), not individual byte or word
        ///                 ordering.
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
                                                   Action? successCallback = null,
                                                   Action<Exception>? errorCallback = null,
                                                   TextEncoding textEncoding = TextEncoding.Ascii,
                                                   TimeSpan? operationTimeout = null);

        #endregion

        #endregion
    }
}