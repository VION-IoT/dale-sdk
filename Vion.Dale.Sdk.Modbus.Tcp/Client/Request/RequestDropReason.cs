using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Why a request was dropped before it could be executed.
    /// </summary>
    [PublicApi]
    public enum RequestDropReason
    {
        /// <summary>The queue was at capacity and the overflow policy evicted this request.</summary>
        QueueFull,

        /// <summary>The client was disposed, so the queue no longer accepts work.</summary>
        ClientDisposed,
    }
}