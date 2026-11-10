using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Reads wired M-Bus meters through the service provider that owns the M-Bus master. The provider returns the
    ///     telegrams as the meter sent them; this contract checks and decodes them, and a logic block picks values from
    ///     the readout with <see cref="MBusSelector" />s.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Declare the binding as <c>IMBus</c> with <c>[ServiceProviderContractBinding]</c>, enable it, and read on a
    ///         timer. A contract starts disabled and executes no read until <see cref="IsEnabled" /> is set.
    ///     </para>
    ///     <para>
    ///         One read is in flight per binding. A <see cref="Read" /> while the binding's previous read is pending is
    ///         dropped: it publishes nothing, invokes neither callback and is counted in <c>Link.BusyCount</c>. The
    ///         pending read's callbacks still arrive. One read returns the whole meter, so read once per interval.
    ///     </para>
    ///     <para>
    ///         Every failure reaches the error callback as an <see cref="MBusException" /> whose <c>Outcome</c> equals
    ///         the receipt's. Every M-Bus binding of a runtime shares one handler, and with it a limit of pending reads.
    ///         Expiry is checked by a sweep that runs about once a second.
    ///     </para>
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("MBus")]
    public interface IMBus
    {
        /// <summary>Whether the contract executes reads; <c>false</c> until set. A read already accepted still completes.</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        ///     How long the service provider has to answer a read once it is published, including the time the read
        ///     waits in the provider's line queue behind other meters; 300 s by default.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The value is zero, negative, or longer than <c>int.MaxValue</c>
        ///     milliseconds.
        /// </exception>
        TimeSpan DefaultOperationTimeout { get; set; }

        /// <summary>
        ///     How long a read may take to reach the shared handler before it is completed as <c>Expired</c> unpublished;
        ///     30 s by default, <c>null</c> for no limit.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The value is zero, negative, or longer than <c>int.MaxValue</c>
        ///     milliseconds.
        /// </exception>
        TimeSpan? MaxQueuedAge { get; set; }

        /// <summary>The link to the meter: its state, counts per outcome and of busy reads, and recent latencies.</summary>
        MBusLinkSummary Link { get; }

        /// <summary>
        ///     Reads every telegram of the meter at <paramref name="primaryAddress" />, up to
        ///     <paramref name="maxTelegrams" />, and returns at once; the readout or the failure arrives later through
        ///     <paramref name="dispatcher" />. A read while this binding's previous read is pending does nothing, and an
        ///     argument out of range is refused through the error callback.
        /// </summary>
        /// <param name="primaryAddress">The meter's primary address, 0–250.</param>
        /// <param name="maxTelegrams">
        ///     How many telegrams the meter sends, at least 1; a smaller count leaves the readout
        ///     incomplete.
        /// </param>
        /// <param name="dispatcher">The actor the callbacks run on, normally the logic block itself.</param>
        /// <param name="successCallback">Receives the readout and the receipt.</param>
        /// <param name="errorCallback">Receives the failure, an <see cref="MBusException" />, and the receipt.</param>
        /// <param name="operationTimeout">
        ///     This read's operation timeout, or <c>null</c> for <see cref="DefaultOperationTimeout" />
        ///     .
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="operationTimeout" /> is zero, negative, or longer than
        ///     <c>int.MaxValue</c> milliseconds.
        /// </exception>
        void Read(int primaryAddress,
                  int maxTelegrams,
                  IActorDispatcher dispatcher,
                  Action<MBusReadout, MBusReceipt> successCallback,
                  Action<Exception, MBusReceipt>? errorCallback = null,
                  TimeSpan? operationTimeout = null);
    }
}