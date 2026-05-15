using System;
using Vion.Contracts.FlatBuffers.Hw.Modbus;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Factory for creating Modbus RTU read and write requests.
    /// </summary>
    public interface IModbusRtuRequestFactory
    {
        /// <summary>
        ///     Creates a read request for a Modbus RTU device.
        /// </summary>
        /// <typeparam name="T">The type of data to be returned after processing the response.</typeparam>
        /// <param name="functionCode">The Modbus function code specifying the type of read operation.</param>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the target device.</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers, coils or discrete inputs to read.</param>
        /// <param name="operationTimeout">The timeout duration for the operation.</param>
        /// <param name="processResponse">A function to process the raw response bytes into the desired type.</param>
        /// <param name="successCallback">The callback invoked with the processed result on successful completion.</param>
        /// <param name="errorCallback">The callback invoked with the exception on failure.</param>
        /// <returns>A <see cref="ReadModbusRtuRequest" /> configured with the specified parameters.</returns>
        ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                  int unitIdentifier,
                                                  ushort startingAddress,
                                                  ushort quantity,
                                                  TimeSpan operationTimeout,
                                                  Func<Memory<byte>, T[]> processResponse,
                                                  Action<T[]> successCallback,
                                                  Action<Exception>? errorCallback);

        /// <summary>
        ///     Creates a read request for a Modbus RTU device that returns a single value.
        /// </summary>
        /// <typeparam name="T">The type of data to be returned after processing the response.</typeparam>
        /// <param name="functionCode">The Modbus function code specifying the type of read operation.</param>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the target device.</param>
        /// <param name="startingAddress">The starting address to read from.</param>
        /// <param name="quantity">The number of registers, coils or discrete inputs to read.</param>
        /// <param name="operationTimeout">The timeout duration for the operation.</param>
        /// <param name="processResponse">A function to process the raw response bytes into the desired type.</param>
        /// <param name="successCallback">The callback invoked with the processed result on successful completion.</param>
        /// <param name="errorCallback">The callback invoked with the exception on failure.</param>
        /// <returns>A <see cref="ReadModbusRtuRequest" /> configured with the specified parameters.</returns>
        ReadModbusRtuRequest CreateReadRequest<T>(ModbusFunctionCode functionCode,
                                                  int unitIdentifier,
                                                  ushort startingAddress,
                                                  ushort quantity,
                                                  TimeSpan operationTimeout,
                                                  Func<Memory<byte>, T> processResponse,
                                                  Action<T> successCallback,
                                                  Action<Exception>? errorCallback);

        /// <summary>
        ///     Creates a write request for a Modbus RTU device.
        /// </summary>
        /// <param name="functionCode">The Modbus function code specifying the type of write operation.</param>
        /// <param name="unitIdentifier">The unit identifier (slave address) of the target device.</param>
        /// <param name="address">The address to write to.</param>
        /// <param name="data">The byte array containing the data to write to the device.</param>
        /// <param name="operationTimeout">The timeout duration for the operation.</param>
        /// <param name="successCallback">The callback invoked on successful completion.</param>
        /// <param name="errorCallback">The callback invoked with the exception on failure.</param>
        /// <returns>A <see cref="WriteModbusRtuRequest" /> configured with the specified parameters.</returns>
        WriteModbusRtuRequest CreateWriteRequest(ModbusFunctionCode functionCode,
                                                 int unitIdentifier,
                                                 ushort address,
                                                 byte[] data,
                                                 TimeSpan operationTimeout,
                                                 Action? successCallback,
                                                 Action<Exception>? errorCallback);
    }
}