using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The contract's verdict on the link to its meter, from the last read whose outcome says something about it.
    /// </summary>
    [PublicApi]
    public enum MBusLinkState
    {
        /// <summary>No read has said anything about the meter yet.</summary>
        [EnumLabel("Unknown")]
        [Severity(StatusSeverity.Neutral)]
        Unknown,

        /// <summary>The meter last answered with telegrams that passed their checks.</summary>
        [EnumLabel("Online")]
        [Severity(StatusSeverity.Success)]
        Online,

        /// <summary>
        ///     The last read got no answer, a garbled one, an unavailable line, a timeout or telegrams that failed their
        ///     checks.
        /// </summary>
        [EnumLabel("Faulted")]
        [Severity(StatusSeverity.Error)]
        Faulted,
    }
}