using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation
{
    /// <summary>
    ///     Provides an abstraction for Modbus TCP client operations for testability.
    /// </summary>
    public interface IModbusTcpClientProxy : IDisposable
    {
        /// <summary>
        ///     Gets a value indicating whether the client is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        ///     Connects to the Modbus TCP server at the specified IP address and port.
        /// </summary>
        /// <param name="ipAddress">The IP address to connect to.</param>
        /// <param name="port">The port number to connect to.</param>
        /// <param name="connectionTimeout">The maximum time to wait for the connection to be established.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation is canceled via the cancellation token.</exception>
        /// <exception cref="ConnectionTimeoutException">Thrown when the connection attempt exceeds the configured timeout period.</exception>
        Task ConnectAsync(IPAddress ipAddress, int port, TimeSpan connectionTimeout, CancellationToken cancellationToken);

        /// <summary>
        ///     Disconnects from the Modbus TCP server.
        /// </summary>
        void Disconnect();

        /// <summary>
        ///     Reads discrete inputs from the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation, containing a reference to the reused response buffer with the discrete input data.</returns>
        /// <remarks>
        ///     WARNING: The returned Memory&lt;byte&gt; references an internal reused buffer.
        ///     The data is only valid until the next Modbus operation on this client instance.
        /// </remarks>
        Task<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken);

        /// <summary>
        ///     Reads coils from the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation, containing a reference to the reused response buffer with the coil data.</returns>
        /// <remarks>
        ///     WARNING: The returned Memory&lt;byte&gt; references an internal reused buffer.
        ///     The data is only valid until the next Modbus operation on this client instance.
        /// </remarks>
        Task<Memory<byte>> ReadCoilsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a single coil to the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the coil to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple coils to the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The coil values to write.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, CancellationToken cancellationToken);

        /// <summary>
        ///     Reads input registers from the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation, containing a reference to the reused response buffer with the input register data.</returns>
        /// <remarks>
        ///     WARNING: The returned Memory&lt;byte&gt; references an internal reused buffer.
        ///     The data is only valid until the next Modbus operation on this client instance.
        /// </remarks>
        Task<Memory<byte>> ReadInputRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken);

        /// <summary>
        ///     Reads holding registers from the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation, containing a reference to the reused response buffer with the holding register data.</returns>
        /// <remarks>
        ///     WARNING: The returned Memory&lt;byte&gt; references an internal reused buffer.
        ///     The data is only valid until the next Modbus operation on this client instance.
        /// </remarks>
        Task<Memory<byte>> ReadHoldingRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes a single register to the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="registerAddress">The address of the register to write.</param>
        /// <param name="value">The register value as bytes (2 bytes per register).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteSingleRegisterAsync(byte unitIdentifier, ushort registerAddress, byte[] value, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes multiple registers to the Modbus device.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address to write to.</param>
        /// <param name="values">The register values as bytes (2 bytes per register).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteMultipleRegistersAsync(byte unitIdentifier, ushort startingAddress, byte[] values, CancellationToken cancellationToken);
    }
}