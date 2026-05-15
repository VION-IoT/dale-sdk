using System;
using Vion.Contracts.FlatBuffers.Hw.Modbus;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Represents a request to read data from a Modbus RTU device.
    /// </summary>
    /// <param name="FunctionCode">The Modbus function code.</param>
    /// <param name="UnitId">The unit identifier of the Modbus device.</param>
    /// <param name="StartingAddress">The starting address to read from.</param>
    /// <param name="Quantity">The number of registers or coils to read.</param>
    /// <param name="CreatedAt">The UTC time when the request was created.</param>
    /// <param name="ExpiresAt">The UTC time when the request expires.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response data.</param>
    public readonly record struct ReadModbusRtuRequest(
        ModbusFunctionCode FunctionCode,
        byte UnitId,
        ushort StartingAddress,
        ushort Quantity,
        DateTime CreatedAt,
        DateTime ExpiresAt,
        Guid CorrelationId,
        Action<byte[]?, Exception?> Callback);

    /// <summary>
    ///     Represents a response from a Modbus RTU read operation.
    /// </summary>
    /// <param name="Data">The data read from the Modbus device, or null if an error occurred.</param>
    /// <param name="Exception">The exception that occurred, or null if the operation was successful.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response data.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    public readonly record struct ReadModbusRtuResponse(byte[]? Data, Exception? Exception, Action<byte[]?, Exception?> Callback, Guid CorrelationId);

    /// <summary>
    ///     Represents a request to write data to a Modbus RTU device.
    /// </summary>
    /// <param name="FunctionCode">The Modbus function code.</param>
    /// <param name="UnitId">The unit identifier of the Modbus device.</param>
    /// <param name="Address">The address to write to.</param>
    /// <param name="Data">The data to write.</param>
    /// <param name="CreatedAt">The UTC time when the request was created.</param>
    /// <param name="ExpiresAt">The UTC time when the request expires.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response.</param>
    public readonly record struct WriteModbusRtuRequest(
        ModbusFunctionCode FunctionCode,
        byte UnitId,
        ushort Address,
        byte[] Data,
        DateTime CreatedAt,
        DateTime ExpiresAt,
        Guid CorrelationId,
        Action<Exception?> Callback);

    /// <summary>
    ///     Represents a response from a Modbus RTU write operation.
    /// </summary>
    /// <param name="Exception">The exception that occurred, or null if the operation was successful.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    public readonly record struct WriteModbusRtuResponse(Exception? Exception, Action<Exception?> Callback, Guid CorrelationId);

    /// <summary>
    ///     Represents a message to trigger checking of expired requests.
    /// </summary>
    public readonly record struct CheckExpiredRequests;
}