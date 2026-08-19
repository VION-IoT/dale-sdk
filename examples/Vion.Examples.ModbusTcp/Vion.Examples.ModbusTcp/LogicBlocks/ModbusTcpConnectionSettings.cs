using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.ModbusTcp.LogicBlocks
{
    /// <summary>
    ///     Where the client talks and how long it waits — one editable struct rather than five flat
    ///     properties, so a change of endpoint is one edit and the block has one place to react to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The shape is the one the SDK's first consumer arrived at independently (their
    ///         <c>ModbusConnection</c> / <c>IModbusTcpConnection</c>, "one SP, one setter, one reconfigure
    ///         chokepoint"), reproduced here so the pattern has a worked example in this repo. The per-field
    ///         <see cref="StructFieldAttribute" /> annotations carry the labels, ranges and tooltips into the
    ///         dashboard form, and the two <see cref="TimeSpan" /> fields render as duration controls — the
    ///         same primitive a top-level <c>TimeSpan</c> property maps to. Milliseconds-as-int would throw
    ///         that away and make the block, not the platform, responsible for the unit.
    ///     </para>
    ///     <para>
    ///         Composite properties do not render as a dashboard tile (DALE032), so the property carrying this
    ///         takes no <c>Importance</c>; the headline is the block's own scalar status.
    ///     </para>
    /// </remarks>
    public readonly record struct ModbusTcpConnectionSettings(
        [StructField(Title = "Server address", StringFormat = StringFormats.Ipv4, Description = "IPv4 address of the Modbus TCP server.")]
        string IpAddress,
        [StructField(Title = "Port", Minimum = 1, Maximum = 65535, Description = "502 is the standard Modbus TCP port; the bundled sim server uses 15020.")]
        int Port,
        [StructField(Title = "Unit id", Minimum = 0, Maximum = 255, Description = "Slave / unit identifier sent with every request.")]
        int UnitId,
        [StructField(Title = "Connection timeout",
                     Description =
                         "How long to wait for the TCP handshake before abandoning the attempt. An address the network drops rather than refuses is what makes this visible.")]
        TimeSpan ConnectionTimeout,
        [StructField(Title = "Operation timeout", Description = "How long one request may take on the wire before it is abandoned.")]
        TimeSpan OperationTimeout);

    /// <summary>
    ///     The three knobs on the client's link policy — what it does when the device stops answering.
    ///     Separate from <see cref="ModbusTcpConnectionSettings" /> because they are edited for different
    ///     reasons: the endpoint is what you change to point the tool at a device, the policy is what you
    ///     change to watch the client behave under failure.
    /// </summary>
    /// <remarks>
    ///     These map one-to-one onto <c>IModbusClient.MaxQueuedAge</c>, <c>ConnectBackoff</c> and
    ///     <c>ConnectBackoffMax</c>, nullability included: a null <see cref="MaxQueuedAge" /> switches the
    ///     check off, which is the SDK's own meaning and needs no <c>0 = off</c> translation in between.
    /// </remarks>
    public readonly record struct ModbusTcpLinkPolicy(
        [StructField(Title = "Max queued age",
                     Description =
                         "A request that has waited longer than this is dropped instead of sent — set it near the poll interval so a backlog sheds stale reads rather than replaying them. Empty switches the check off.")]
        TimeSpan? MaxQueuedAge,
        [StructField(Title = "Connect backoff", Description = "The wait before the second connect attempt after consecutive failures. It doubles per failure up to the maximum.")]
        TimeSpan ConnectBackoff,
        [StructField(Title = "Connect backoff max",
                     Description =
                         "The ceiling the backoff doubles up to. Setting it equal to the backoff makes the wait a constant, which is the floor — there is no off switch.")]
        TimeSpan ConnectBackoffMax);
}