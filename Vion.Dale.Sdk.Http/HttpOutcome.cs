using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     How a single HTTP request ended.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The first four values mean a server answered: <see cref="ClientError" /> and <see cref="ServerError" />
    ///         carry the status it answered with on <see cref="HttpReceipt.StatusCode" />. <see cref="Timeout" /> and
    ///         <see cref="TransportError" /> mean no usable answer arrived — a <see cref="TransportError" /> keeps the 2xx
    ///         status when the body broke after the headers — and <see cref="Invalid" /> that the request was
    ///         never handed to the transport at all.
    ///     </para>
    ///     <para>
    ///         A status of 500 or above is a <see cref="ServerError" />; every other status outside 2xx — a 4xx, or a 1xx
    ///         or 3xx the platform did not follow — is a <see cref="ClientError" />.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public enum HttpOutcome
    {
        /// <summary>The server answered with a 2xx status, and its content was read where the request reads any.</summary>
        [EnumLabel("Success")]
        [Severity(StatusSeverity.Success)]
        Success,

        /// <summary>The server answered with a status outside 2xx and below 500 — the request was not what it accepts.</summary>
        [EnumLabel("Client error")]
        [Severity(StatusSeverity.Warning)]
        ClientError,

        /// <summary>The server answered with a status of 500 or above.</summary>
        [EnumLabel("Server error")]
        [Severity(StatusSeverity.Error)]
        ServerError,

        /// <summary>The server answered with a 2xx status, but its body was absent, malformed or deserialized to null.</summary>
        [EnumLabel("Content error")]
        [Severity(StatusSeverity.Error)]
        ContentError,

        /// <summary>The request's own timeout, or the client's, elapsed before the response arrived.</summary>
        [EnumLabel("Timeout")]
        [Severity(StatusSeverity.Error)]
        Timeout,

        /// <summary>The connection, the TLS handshake or the response stream failed.</summary>
        [EnumLabel("Transport error")]
        [Severity(StatusSeverity.Error)]
        TransportError,

        /// <summary>
        ///     Not sent: the client could not be built, or it refused the request — a URL no base address makes absolute, a
        ///     disposed client, a request message sent before.
        /// </summary>
        [EnumLabel("Invalid")]
        [Severity(StatusSeverity.Warning)]
        Invalid,
    }
}