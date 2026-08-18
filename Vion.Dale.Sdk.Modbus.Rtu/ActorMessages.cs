using System;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

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
    /// <param name="OperationTimeout">How long the device has to answer once the request has been published.</param>
    /// <param name="MaxQueuedAge">
    ///     How stale the request may be when the handler picks it up, or <c>null</c> to publish it however long the hop
    ///     took.
    /// </param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response data.</param>
    public readonly record struct ReadModbusRtuRequest(
        ModbusFunctionCode FunctionCode,
        byte UnitId,
        ushort StartingAddress,
        ushort Quantity,
        DateTime CreatedAt,
        TimeSpan OperationTimeout,
        TimeSpan? MaxQueuedAge,
        Guid CorrelationId,
        Action<byte[]?, Exception?, ModbusReceipt> Callback);

    /// <summary>
    ///     Represents a response from a Modbus RTU read operation.
    /// </summary>
    /// <param name="Data">The data read from the Modbus device, or null if an error occurred.</param>
    /// <param name="Exception">The exception that occurred, or null if the operation was successful.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response data.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="CreatedAt">The UTC time when the request was created, carried through so the receipt is self-contained.</param>
    /// <param name="PublishedAt">The UTC time the request was handed to MQTT, or null if it never was.</param>
    /// <param name="ReceivedAt">The UTC time the response or failure was observed by the handler.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale.</param>
    /// <param name="Outcome">How the operation ended, as classified by the handler.</param>
    public readonly record struct ReadModbusRtuResponse(
        byte[]? Data,
        Exception? Exception,
        Action<byte[]?, Exception?, ModbusReceipt> Callback,
        Guid CorrelationId,
        DateTime CreatedAt,
        DateTime? PublishedAt,
        DateTime ReceivedAt,
        long ReceivedTimestamp,
        ModbusOutcome Outcome)
    {
        /// <summary>Builds the receipt handed to the logic block's callback.</summary>
        public ModbusReceipt ToReceipt()
        {
            return ModbusRtuReceiptBuilder.Build(CreatedAt, PublishedAt, ReceivedAt, ReceivedTimestamp, Outcome);
        }
    }

    /// <summary>
    ///     Represents a request to write data to a Modbus RTU device.
    /// </summary>
    /// <param name="FunctionCode">The Modbus function code.</param>
    /// <param name="UnitId">The unit identifier of the Modbus device.</param>
    /// <param name="Address">The address to write to.</param>
    /// <param name="Data">The data to write.</param>
    /// <param name="CreatedAt">The UTC time when the request was created.</param>
    /// <param name="OperationTimeout">How long the device has to answer once the request has been published.</param>
    /// <param name="MaxQueuedAge">
    ///     How stale the request may be when the handler picks it up, or <c>null</c> to publish it however long the hop
    ///     took.
    /// </param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response.</param>
    public readonly record struct WriteModbusRtuRequest(
        ModbusFunctionCode FunctionCode,
        byte UnitId,
        ushort Address,
        byte[] Data,
        DateTime CreatedAt,
        TimeSpan OperationTimeout,
        TimeSpan? MaxQueuedAge,
        Guid CorrelationId,
        Action<Exception?, ModbusReceipt> Callback);

    /// <summary>
    ///     Represents a response from a Modbus RTU write operation.
    /// </summary>
    /// <param name="Exception">The exception that occurred, or null if the operation was successful.</param>
    /// <param name="Callback">The callback for the logic block I/O to invoke with the response.</param>
    /// <param name="CorrelationId">The correlation ID used to match requests with responses.</param>
    /// <param name="CreatedAt">The UTC time when the request was created, carried through so the receipt is self-contained.</param>
    /// <param name="PublishedAt">The UTC time the request was handed to MQTT, or null if it never was.</param>
    /// <param name="ReceivedAt">The UTC time the response or failure was observed by the handler.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale.</param>
    /// <param name="Outcome">How the operation ended, as classified by the handler.</param>
    public readonly record struct WriteModbusRtuResponse(
        Exception? Exception,
        Action<Exception?, ModbusReceipt> Callback,
        Guid CorrelationId,
        DateTime CreatedAt,
        DateTime? PublishedAt,
        DateTime ReceivedAt,
        long ReceivedTimestamp,
        ModbusOutcome Outcome)
    {
        /// <summary>Builds the receipt handed to the logic block's callback.</summary>
        public ModbusReceipt ToReceipt()
        {
            return ModbusRtuReceiptBuilder.Build(CreatedAt, PublishedAt, ReceivedAt, ReceivedTimestamp, Outcome);
        }
    }

    /// <summary>
    ///     Represents a message to trigger checking of expired requests.
    /// </summary>
    public readonly record struct CheckExpiredRequests;

    /// <summary>
    ///     Splits an RTU transaction's instants into the receipt's two durations.
    /// </summary>
    /// <remarks>
    ///     A published request splits at the publish: the hop from the block to the shared handler is the queued wait,
    ///     the wire exchange is the round trip. A request that never reached MQTT — rejected by the pending limit,
    ///     unmapped, or aged out — was never dispatched, so all of its elapsed time is queued wait and its round trip
    ///     is zero.
    /// </remarks>
    internal static class ModbusRtuReceiptBuilder
    {
        public static ModbusReceipt Build(DateTime createdAt, DateTime? publishedAt, DateTime receivedAt, long receivedTimestamp, ModbusOutcome outcome)
        {
            var queuedWait = Clamp((publishedAt ?? receivedAt) - createdAt);
            var roundTrip = publishedAt is { } published ? Clamp(receivedAt - published) : TimeSpan.Zero;

            return new ModbusReceipt(receivedAt, receivedTimestamp, roundTrip, queuedWait, outcome);
        }

        private static TimeSpan Clamp(TimeSpan value)
        {
            return value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }
    }
}