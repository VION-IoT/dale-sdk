using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation
{
    /// <summary>
    ///     Provides a wrapper for Modbus TCP client operations with data conversion and validation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All read and write operations automatically establish a connection if not already connected.
    ///     </para>
    ///     <para>
    ///         Read, write, and disconnect operations are not thread-safe. Concurrent calls to these methods will lead to data corruption or unexpected behavior.
    ///     </para>
    ///     <para>
    ///         Property setters (<see cref="IpAddress" />, <see cref="Port" />, <see cref="ConnectionTimeout" />) can be called concurrently with other operations.
    ///         Changes take effect on the next connection attempt (for <see cref="ConnectionTimeout" />) or the next read/write operation (for <see cref="IpAddress" /> and
    ///         <see cref="Port" />).
    ///     </para>
    /// </remarks>
    public interface IModbusTcpClientWrapper : IDisposable
    {
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
        /// <remarks>
        ///     Changes to this property do not trigger an immediate reconnect. The new port will be used when the next read or write operation is executed.
        /// </remarks>
        int Port { get; set; }

        /// <summary>
        ///     Gets or sets the IP address of the Modbus TCP server.
        /// </summary>
        /// <remarks>
        ///     Changes to this property do not trigger an immediate reconnect. The new IP address will be used when the next read or write operation is executed.
        /// </remarks>
        IPAddress? IpAddress { get; set; }

        /// <summary>
        ///     Disconnects from the Modbus TCP device if connected.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>
        ///     A task that represents the asynchronous operation.
        /// </returns>
        /// <remarks>
        ///     This method is idempotent - calling it when already disconnected has no effect.
        /// </remarks>
        Task DisconnectAsync(CancellationToken cancellationToken);

        #endregion

        #region ModbusDataAccess

        #region DiscreteInputs

        /// <summary>
        ///     Reads discrete inputs from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of boolean values representing the discrete input states.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="InvalidBitQuantityException">
        ///     Thrown when the server's response contains fewer bits than the requested <paramref name="quantity" /> (indicating a protocol violation or malformed response).
        ///     For example, if the quantity is 17, the server should return 3 bytes (24 bits), with the last 7 bits being padding and ignored.
        ///     If only 2 bytes (16 bits) are returned, this exception is thrown.
        /// </exception>
        Task<bool[]> ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken);

        #endregion

        #region Coils

        /// <summary>
        ///     Reads coils from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of boolean values representing the coil states.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="InvalidBitQuantityException">
        ///     Thrown when the server's response contains fewer bits than the requested <paramref name="quantity" /> (indicating a protocol violation or malformed response).
        ///     For example, if the quantity is 17, the server should return 3 bytes (24 bits), with the last 7 bits being padding and ignored.
        ///     If only 2 bytes (16 bits) are returned, this exception is thrown.
        /// </exception>
        Task<bool[]> ReadCoilsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a single coil to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the coil to write.</param>
        /// <param name="value">The value to write to the coil.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, TimeSpan operationTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple coils to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write to the coils.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, TimeSpan operationTimeout, CancellationToken cancellationToken);

        #endregion

        #region InputRegisters

        /// <summary>
        ///     Reads input registers as raw bytes from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a byte array with the raw register data.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task<byte[]> ReadInputRegistersRawAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as signed 16-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 16-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 2 (bytes per 16-bit value).
        /// </exception>
        Task<short[]> ReadInputRegistersAsShortAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     ushort quantity,
                                                     ByteOrder byteOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as unsigned 16-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 16-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 2 (bytes per 16-bit value).
        /// </exception>
        Task<ushort[]> ReadInputRegistersAsUShortAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       ushort quantity,
                                                       ByteOrder byteOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as signed 32-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 32-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<int[]> ReadInputRegistersAsIntAsync(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint count,
                                                 ByteOrder byteOrder,
                                                 WordOrder32 wordOrder,
                                                 TimeSpan operationTimeout,
                                                 CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as unsigned 32-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 32-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<uint[]> ReadInputRegistersAsUIntAsync(int unitIdentifier,
                                                   ushort startingAddress,
                                                   uint count,
                                                   ByteOrder byteOrder,
                                                   WordOrder32 wordOrder,
                                                   TimeSpan operationTimeout,
                                                   CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as 32-bit floating-point numbers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit floating-point numbers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of 32-bit floating-point numbers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<float[]> ReadInputRegistersAsFloatAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     uint count,
                                                     ByteOrder byteOrder,
                                                     WordOrder32 wordOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as signed 64-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 64-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<long[]> ReadInputRegistersAsLongAsync(int unitIdentifier,
                                                   ushort startingAddress,
                                                   uint count,
                                                   ByteOrder byteOrder,
                                                   WordOrder64 wordOrder,
                                                   TimeSpan operationTimeout,
                                                   CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as unsigned 64-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 64-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<ulong[]> ReadInputRegistersAsULongAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     uint count,
                                                     ByteOrder byteOrder,
                                                     WordOrder64 wordOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as 64-bit floating-point numbers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit floating-point numbers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of 64-bit floating-point numbers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<double[]> ReadInputRegistersAsDoubleAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       uint count,
                                                       ByteOrder byteOrder,
                                                       WordOrder64 wordOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers as a string from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="textEncoding">The text encoding to use for decoding the string.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the decoded string.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        Task<string> ReadInputRegistersAsStringAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     ushort quantity,
                                                     TextEncoding textEncoding,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        #endregion

        #region HoldingRegisters

        /// <summary>
        ///     Reads holding registers as raw bytes from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a byte array with the raw register data.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task<byte[]> ReadHoldingRegistersRawAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as signed 16-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 16-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 2 (bytes per 16-bit value).
        /// </exception>
        Task<short[]> ReadHoldingRegistersAsShortAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       ushort quantity,
                                                       ByteOrder byteOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as unsigned 16-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 16-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 2 (bytes per 16-bit value).
        /// </exception>
        Task<ushort[]> ReadHoldingRegistersAsUShortAsync(int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         ByteOrder byteOrder,
                                                         TimeSpan operationTimeout,
                                                         CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as signed 32-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 32-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<int[]> ReadHoldingRegistersAsIntAsync(int unitIdentifier,
                                                   ushort startingAddress,
                                                   uint count,
                                                   ByteOrder byteOrder,
                                                   WordOrder32 wordOrder,
                                                   TimeSpan operationTimeout,
                                                   CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as unsigned 32-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 32-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<uint[]> ReadHoldingRegistersAsUIntAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     uint count,
                                                     ByteOrder byteOrder,
                                                     WordOrder32 wordOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as 32-bit floating-point numbers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 32-bit floating-point numbers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of 32-bit floating-point numbers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 4 (bytes per 32-bit value).
        /// </exception>
        Task<float[]> ReadHoldingRegistersAsFloatAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       uint count,
                                                       ByteOrder byteOrder,
                                                       WordOrder32 wordOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as signed 64-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of signed 64-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<long[]> ReadHoldingRegistersAsLongAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     uint count,
                                                     ByteOrder byteOrder,
                                                     WordOrder64 wordOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as unsigned 64-bit integers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit integers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of unsigned 64-bit integers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<ulong[]> ReadHoldingRegistersAsULongAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       uint count,
                                                       ByteOrder byteOrder,
                                                       WordOrder64 wordOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as 64-bit floating-point numbers from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="count">The number of 64-bit floating-point numbers to read.</param>
        /// <param name="byteOrder">The byte order the data is received in.</param>
        /// <param name="wordOrder">The word order the data is received in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array of 64-bit floating-point numbers.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when the response byte count is not a multiple of 8 (bytes per 64-bit value).
        /// </exception>
        Task<double[]> ReadHoldingRegistersAsDoubleAsync(int unitIdentifier,
                                                         ushort startingAddress,
                                                         uint count,
                                                         ByteOrder byteOrder,
                                                         WordOrder64 wordOrder,
                                                         TimeSpan operationTimeout,
                                                         CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers as a string from a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="textEncoding">The text encoding to use for decoding the string.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the decoded string.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task<string> ReadHoldingRegistersAsStringAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       ushort quantity,
                                                       TextEncoding textEncoding,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a single holding register as a signed 16-bit integer to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the register to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteSingleHoldingRegisterAsync(int unitIdentifier,
                                             ushort registerAddress,
                                             short value,
                                             ByteOrder byteOrder,
                                             TimeSpan operationTimeout,
                                             CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a single holding register as an unsigned 16-bit integer to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the register to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteSingleHoldingRegisterAsync(int unitIdentifier,
                                             ushort registerAddress,
                                             ushort value,
                                             ByteOrder byteOrder,
                                             TimeSpan operationTimeout,
                                             CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as raw bytes to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The raw byte values to write.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersRawAsync(int unitIdentifier, ushort startingAddress, byte[] values, TimeSpan operationTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as signed 16-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsShortAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       short[] values,
                                                       ByteOrder byteOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 16-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsUShortAsync(int unitIdentifier,
                                                        ushort startingAddress,
                                                        ushort[] values,
                                                        ByteOrder byteOrder,
                                                        TimeSpan operationTimeout,
                                                        CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as signed 32-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsIntAsync(int unitIdentifier,
                                                     ushort startingAddress,
                                                     int[] values,
                                                     ByteOrder byteOrder,
                                                     WordOrder32 wordOrder,
                                                     TimeSpan operationTimeout,
                                                     CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 32-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsUIntAsync(int unitIdentifier,
                                                      ushort startingAddress,
                                                      uint[] values,
                                                      ByteOrder byteOrder,
                                                      WordOrder32 wordOrder,
                                                      TimeSpan operationTimeout,
                                                      CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as 32-bit floating-point numbers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsFloatAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       float[] values,
                                                       ByteOrder byteOrder,
                                                       WordOrder32 wordOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as signed 64-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsLongAsync(int unitIdentifier,
                                                      ushort startingAddress,
                                                      long[] values,
                                                      ByteOrder byteOrder,
                                                      WordOrder64 wordOrder,
                                                      TimeSpan operationTimeout,
                                                      CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as unsigned 64-bit integers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsULongAsync(int unitIdentifier,
                                                       ushort startingAddress,
                                                       ulong[] values,
                                                       ByteOrder byteOrder,
                                                       WordOrder64 wordOrder,
                                                       TimeSpan operationTimeout,
                                                       CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as 64-bit floating-point numbers to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The values to write.</param>
        /// <param name="byteOrder">The byte order to write in.</param>
        /// <param name="wordOrder">The word order to write in.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsDoubleAsync(int unitIdentifier,
                                                        ushort startingAddress,
                                                        double[] values,
                                                        ByteOrder byteOrder,
                                                        WordOrder64 wordOrder,
                                                        TimeSpan operationTimeout,
                                                        CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple holding registers as a string to a Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="value">The string value to write.</param>
        /// <param name="textEncoding">The text encoding to use for encoding the string.</param>
        /// <param name="operationTimeout">The maximum time allowed for the Modbus operation before it is canceled.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified (typically from casting or deserialization).
        /// </exception>
        /// <exception cref="IpAddressNotSetException">
        ///     Thrown when the IP address has not been set.
        /// </exception>
        /// <exception cref="ConnectionTimeoutException">
        ///     Thrown when the connection attempt exceeds the configured connection timeout.
        /// </exception>
        /// <exception cref="OperationTimeoutException">
        ///     Thrown when the operation exceeds the specified <paramref name="operationTimeout" />.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        ///     Thrown when the operation is canceled via the cancellation token.
        /// </exception>
        /// <exception cref="ModbusException">
        ///     Thrown when the Modbus device returns an error response.
        /// </exception>
        Task WriteMultipleHoldingRegistersAsStringAsync(int unitIdentifier,
                                                        ushort startingAddress,
                                                        string value,
                                                        TextEncoding textEncoding,
                                                        TimeSpan operationTimeout,
                                                        CancellationToken cancellationToken);

        #endregion

        #endregion
    }
}