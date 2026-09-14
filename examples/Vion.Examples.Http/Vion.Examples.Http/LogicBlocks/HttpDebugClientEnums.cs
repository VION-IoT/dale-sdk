using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Http.LogicBlocks
{
    /// <summary>
    ///     The request method a request is sent with, or a route answers.
    /// </summary>
    public enum RequestMethod
    {
        [EnumLabel("GET")]
        Get,

        [EnumLabel("POST")]
        Post,

        [EnumLabel("PUT")]
        Put,

        [EnumLabel("PATCH")]
        Patch,

        [EnumLabel("DELETE")]
        Delete,

        [EnumLabel("HEAD")]
        Head,

        [EnumLabel("OPTIONS")]
        Options,
    }

    /// <summary>
    ///     How the last request ended — the one-glance verdict the debug client leads with.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="HttpError" /> and <see cref="Failed" /> are kept apart because they point at different places:
    ///         an HTTP error means the server was reached and answered with a status outside 2xx, a failure means no answer
    ///         arrived at all — a refused connection, an unknown host, a reset.
    ///     </para>
    ///     <para>
    ///         <see cref="Invalid" /> is the block's own verdict, not the server's: the request could not be built from what
    ///         was typed, so nothing was sent and nothing about the server is known.
    ///     </para>
    /// </remarks>
    public enum RequestOutcome
    {
        [Severity(StatusSeverity.Neutral)]
        [EnumLabel("Idle")]
        Idle,

        [Severity(StatusSeverity.Info)]
        [EnumLabel("In flight")]
        InFlight,

        [Severity(StatusSeverity.Success)]
        [EnumLabel("Succeeded")]
        Succeeded,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("HTTP error")]
        HttpError,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Timed out")]
        TimedOut,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Failed")]
        Failed,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Invalid request")]
        Invalid,
    }
}