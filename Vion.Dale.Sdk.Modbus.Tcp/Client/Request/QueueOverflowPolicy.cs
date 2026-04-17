using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Defines the behavior when the request queue is full.
    /// </summary>
    [PublicApi]
    public enum QueueOverflowPolicy
    {
        /// <summary>
        ///     Drops the oldest request in the queue when a new request is enqueued.
        /// </summary>
        DropOldest = 0,

        /// <summary>
        ///     Drops the newest request in the queue (not the one being enqueued) when a new request is enqueued.
        /// </summary>
        DropNewest = 1,

        /// <summary>
        ///     Rejects the new request being enqueued, invoking its error callback immediately.
        /// </summary>
        RejectNew = 2,
    }
}