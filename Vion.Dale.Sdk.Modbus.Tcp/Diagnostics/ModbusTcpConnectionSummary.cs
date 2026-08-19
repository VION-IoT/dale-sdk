using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Diagnostics
{
    /// <summary>
    ///     A point-in-time summary of one Modbus TCP client's socket: whether it is up, and how its connection attempts
    ///     have gone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Separate from the link summary because it is about the transport rather than the device: a socket can be
    ///         up while every request times out, and a device can be reachable across a socket that has been
    ///         re-established several times. Every field is a service-property-legal type, so the whole summary can be
    ///         published as one <c>[ServiceProperty]</c>.
    ///     </para>
    ///     <para>
    ///         The connection is established lazily, inside the first operation that needs it, so
    ///         <c>LastConnectDuration</c> is also part of that operation's <c>RoundTrip</c> — subtract it when you want
    ///         the device's own response time.
    ///     </para>
    ///     <para>
    ///         Counts are for the lifetime of the client instance and are never reset — except
    ///         <c>ConsecutiveConnectFailures</c>, which is a run rather than a total.
    ///     </para>
    ///     <para>
    ///         <c>CurrentBackoff</c> and <c>NextAttemptAt</c> are filled once the client is waiting between connection
    ///         attempts, and <c>State</c> is <c>BackingOff</c> while that wait is still running. Both are cleared by a
    ///         successful connect or by a configuration change that supersedes the failures.
    ///     </para>
    /// </remarks>
    /// <param name="State">Whether a socket is currently open.</param>
    /// <param name="LastConnectedAt">When a socket was last established.</param>
    /// <param name="LastConnectFailureAt">When a connection attempt last failed.</param>
    /// <param name="ConnectAttemptCount">How many connection attempts have been made.</param>
    /// <param name="ConnectFailureCount">How many connection attempts have failed.</param>
    /// <param name="ConsecutiveConnectFailures">
    ///     Failed attempts since the last successful one or the last configuration
    ///     change.
    /// </param>
    /// <param name="LastConnectDuration">How long the last successful handshake took.</param>
    /// <param name="CurrentBackoff">How long the client is waiting before its next connection attempt.</param>
    /// <param name="NextAttemptAt">When the client will attempt to connect again.</param>
    [PublicApi]
    public readonly record struct ModbusTcpConnectionSummary(
        [StructField(Title = "Connection state", Description = "Whether a socket is open, closed, or waiting out a backoff.")]
        ModbusTcpConnectionState State,
        [StructField(Title = "Last connected", Description = "When a socket was last established (UTC).")]
        DateTime? LastConnectedAt,
        [StructField(Title = "Last connect failure", Description = "When a connection attempt last failed (UTC).")]
        DateTime? LastConnectFailureAt,
        [StructField(Title = "Connect attempts", Description = "How many connection attempts have been made over the client's lifetime.")]
        long ConnectAttemptCount,
        [StructField(Title = "Failed connects", Description = "How many of those attempts failed.")]
        long ConnectFailureCount,
        [StructField(Title = "Consecutive failed connects", Description = "Failed attempts since the last successful connect or the last configuration change.")]
        int ConsecutiveConnectFailures,
        [StructField(Title = "Last handshake", Description = "How long the last successful connect took; it is also part of that operation's round trip.")]
        TimeSpan? LastConnectDuration,
        [StructField(Title = "Current backoff", Description = "How long the client is waiting before its next connection attempt.")]
        TimeSpan? CurrentBackoff,
        [StructField(Title = "Next attempt", Description = "When the client will attempt to connect again (UTC).")]
        DateTime? NextAttemptAt);
}