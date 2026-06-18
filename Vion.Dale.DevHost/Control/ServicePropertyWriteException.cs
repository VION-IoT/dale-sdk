using System;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     A service-property write the block cannot apply, rejected UP FRONT by the control surface (the trip
    ///     wire against a silently-timed-out 200). Subclasses <see cref="InvalidOperationException" /> so existing
    ///     <c>catch (InvalidOperationException)</c> callers are unaffected, while carrying machine-readable
    ///     <see cref="Reason" /> + <see cref="Property" /> so the HTTP layer can return a structured, actionable
    ///     400 body (rather than a prose-only message a tool has to string-match).
    /// </summary>
    public sealed class ServicePropertyWriteException : InvalidOperationException
    {
        /// <summary>The write targets a service id that is not in the wired network.</summary>
        public const string ReasonUnknownService = "unknownService";

        /// <summary>The service has no property or measuring point by that name.</summary>
        public const string ReasonUnknownMember = "unknownMember";

        /// <summary>The member exists but is read-only (a measuring point, or a property with no public setter).</summary>
        public const string ReasonReadOnly = "readOnly";

        /// <summary>Stable reason code: <c>unknownService</c>, <c>unknownMember</c>, or <c>readOnly</c>.</summary>
        public string Reason { get; }

        /// <summary>The offending member name, when the rejection is about a specific property (null for an unknown service).</summary>
        public string? Property { get; }

        public ServicePropertyWriteException(string reason, string? property, string message) : base(message)
        {
            Reason = reason;
            Property = property;
        }
    }
}