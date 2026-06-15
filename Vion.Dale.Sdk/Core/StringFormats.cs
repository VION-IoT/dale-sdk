namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Well-known JSON-Schema <c>format</c> values for string properties, set via
    ///     <c>StringFormat</c> on <see cref="ServicePropertyAttribute" /> / <see cref="StructFieldAttribute" />.
    ///     Open set — integrators may pass any value; UIs recognize these and fall back to a plain
    ///     text input for unknown ones. Advisory only: the runtime never rejects on format.
    ///     Do NOT use these for <c>DateTime</c> / <c>TimeSpan</c> / <c>Guid</c> values — those are CLR
    ///     types whose format is derived (<c>date-time</c> / <c>duration</c> / <c>uuid</c>); see DALE033.
    /// </summary>
    [PublicApi]
    public static class StringFormats
    {
        /// <summary>IPv4 dotted-quad address, e.g. <c>192.168.1.10</c>.</summary>
        public const string Ipv4 = "ipv4";

        /// <summary>IPv6 address.</summary>
        public const string Ipv6 = "ipv6";

        /// <summary>DNS hostname (RFC 1123 label form).</summary>
        public const string Hostname = "hostname";

        /// <summary>Email address.</summary>
        public const string Email = "email";

        /// <summary>URI / URL string.</summary>
        public const string Uri = "uri";
    }
}
