using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Diagnostics
{
    /// <summary>
    ///     The per-transaction facts of one Modbus read or write, handed to the success and error callback of every
    ///     operation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The instants are taken the moment the response or failure was observed — on the queue consumer for
    ///         Modbus TCP, in the RTU handler the moment the response arrives — and therefore before the callback is
    ///         handed to the logic block's mailbox. A receipt is unaffected by how long the block then takes to
    ///         process it, which is what makes it usable as the age of the value it accompanies.
    ///     </para>
    ///     <para>
    ///         <c>ReceivedAt</c> is wall clock and is what you publish; <c>ReceivedTimestamp</c> is the same instant on
    ///         the monotonic timestamp scale and is what you compare against later. Age a value with
    ///         <c>TimeProvider.GetElapsedTime(receipt.ReceivedTimestamp)</c> — a wall clock can step backwards when the
    ///         gateway's clock is corrected, a monotonic timestamp cannot.
    ///     </para>
    ///     <para>
    ///         <c>QueuedWait</c> and <c>RoundTrip</c> split the total latency into the part you control and the part the
    ///         device controls: <c>QueuedWait</c> is the wait before dispatch (Modbus TCP: time in the client's request
    ///         queue; Modbus RTU: the hop from the block to the shared handler), <c>RoundTrip</c> is dispatch to
    ///         response. A <c>RoundTrip</c> that includes a connection handshake is not separated out — subtract
    ///         <c>Connection.LastConnectDuration</c> if that distinction matters.
    ///     </para>
    ///     <para>
    ///         Ignoring a receipt is a discard: <c>(values, _) =&gt; Power = values[0]</c>.
    ///     </para>
    /// </remarks>
    /// <param name="ReceivedAt">The UTC wall clock instant the response or failure was observed.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale, for ageing the value.</param>
    /// <param name="RoundTrip">The time from dispatching the request to observing the response.</param>
    /// <param name="QueuedWait">The time the request waited locally before it was dispatched.</param>
    /// <param name="Outcome">How the operation ended.</param>
    [PublicApi]
    public readonly record struct ModbusReceipt(DateTime ReceivedAt, long ReceivedTimestamp, TimeSpan RoundTrip, TimeSpan QueuedWait, ModbusOutcome Outcome);
}