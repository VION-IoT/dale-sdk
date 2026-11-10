using System;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Every failed M-Bus read reaches the error callback as this exception; its <see cref="Outcome" /> equals the
    ///     receipt's, which is what a logic block decides on.
    /// </summary>
    [PublicApi]
    public sealed class MBusException : Exception
    {
        /// <summary>How the read ended; the same as the receipt's outcome.</summary>
        public MBusOutcome Outcome { get; }

        /// <summary>The response code the service provider answered with, or <c>null</c> when no answer arrived.</summary>
        public string? ResponseCode { get; }

        /// <summary>
        ///     For a <see cref="MBusOutcome.ProtocolError" />, the telegrams as received, which a logic block can log as a
        ///     capture; empty otherwise.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<byte>> Telegrams { get; }

        internal MBusException(MBusOutcome outcome, string message, string? responseCode = null, IReadOnlyList<byte[]>? telegrams = null) : base(telegrams is { Count: > 0 } ?
            $"{message} Telegrams as received: {string.Join(" / ", telegrams.Select(MBusHex.Format))}" : message)
        {
            Outcome = outcome;
            ResponseCode = responseCode;
            Telegrams = (telegrams ?? []).Select(telegram => (IReadOnlyList<byte>)Array.AsReadOnly(telegram)).ToList().AsReadOnly();
        }
    }
}