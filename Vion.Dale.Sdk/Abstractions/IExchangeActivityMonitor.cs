using System;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Optional, opt-in monitor of exchanges a logic block's SDK clients and servers are carrying off the actor
    ///     system — a Modbus TCP request between its enqueue and its completion reaching the block, an HTTP request
    ///     between the block's call and its callback reaching the block, a request a hosted HTTP server has read in
    ///     full and not yet recorded. When an implementation is registered in the actor system's service provider, those
    ///     packages open an exchange for each and dispose it when it ends; when none is registered (the default,
    ///     including the production runtime and every test kit), nothing extra happens.
    ///     <para>
    ///         Used by the development host's deterministic stepping: while an exchange is open, its result has not yet
    ///         reached any mailbox, so mailbox depth and the in-flight handler count both read zero. Counting it is what
    ///         keeps a stepped settle from returning before the round trip lands. The same opt-in pattern as
    ///         <see cref="IActorActivityMonitor" /> and <see cref="IVirtualSchedule" />.
    ///     </para>
    ///     <para>Implementations must be thread-safe: exchanges open and close on actor, pool and transport threads.</para>
    /// </summary>
    public interface IExchangeActivityMonitor
    {
        /// <summary>
        ///     Records an exchange as open until the returned handle is disposed. Disposing the handle more than once
        ///     closes the exchange once.
        /// </summary>
        /// <param name="description">What the exchange is, as a person reading a failure would name it.</param>
        IDisposable OpenExchange(string description);
    }
}