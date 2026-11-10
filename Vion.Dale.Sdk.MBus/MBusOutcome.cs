using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     How a single M-Bus read ended.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Success</c> sets the link state to <c>Online</c>; <c>NoResponse</c>, <c>Garbled</c>,
    ///         <c>LineUnavailable</c>, <c>Timeout</c> and <c>ProtocolError</c> set it to <c>Faulted</c>. The others are
    ///         decided without the meter — the request aged out, the shared limit was reached, the call or the
    ///         deployment was wrong — and leave the link state as it was.
    ///     </para>
    ///     <para>
    ///         A read refused because the binding's previous read is still pending has no outcome: it invokes neither
    ///         callback and is counted in <see cref="MBusLinkSummary.BusyCount" />.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public enum MBusOutcome
    {
        /// <summary>The meter answered and every telegram passed its checks.</summary>
        [EnumLabel("Success")]
        [Severity(StatusSeverity.Success)]
        Success,

        /// <summary>The meter stayed silent through the service provider's repetitions.</summary>
        [EnumLabel("No response")]
        [Severity(StatusSeverity.Error)]
        NoResponse,

        /// <summary>The service provider saw a checksum, framing, length or collision fault on the line.</summary>
        [EnumLabel("Garbled")]
        [Severity(StatusSeverity.Error)]
        Garbled,

        /// <summary>The service provider's line or terminal was not ready, or its request queue was full.</summary>
        [EnumLabel("Line unavailable")]
        [Severity(StatusSeverity.Error)]
        LineUnavailable,

        /// <summary>No answer arrived within the operation timeout.</summary>
        [EnumLabel("Timeout")]
        [Severity(StatusSeverity.Error)]
        Timeout,

        /// <summary>An answer arrived, but a telegram failed its checks or could not be decoded.</summary>
        [EnumLabel("Protocol error")]
        [Severity(StatusSeverity.Error)]
        ProtocolError,

        /// <summary>Not attempted: the read aged past <see cref="IMBus.MaxQueuedAge" /> before it was published.</summary>
        [EnumLabel("Expired")]
        [Severity(StatusSeverity.Warning)]
        Expired,

        /// <summary>Not attempted: the runtime-wide limit of pending reads was reached.</summary>
        [EnumLabel("Dropped")]
        [Severity(StatusSeverity.Warning)]
        Dropped,

        /// <summary>
        ///     Not attempted, or not understood: an argument was out of range, the binding is not mapped, or the service
        ///     provider and this SDK are of different contract versions.
        /// </summary>
        [EnumLabel("Invalid")]
        [Severity(StatusSeverity.Warning)]
        Invalid,
    }
}