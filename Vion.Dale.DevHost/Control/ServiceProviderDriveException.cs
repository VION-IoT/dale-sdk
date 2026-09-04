using System;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     A drive onto a service-provider value contract that would reach nothing, rejected UP FRONT by the
    ///     control surface — the contract plane's counterpart of <see cref="ServicePropertyWriteException" />.
    ///     Subclasses <see cref="InvalidOperationException" /> so existing
    ///     <c>catch (InvalidOperationException)</c> callers are unaffected, while carrying a machine-readable
    ///     <see cref="Reason" /> and the addressed <see cref="Contract" /> so the HTTP layer can return a
    ///     structured 400 rather than a 200 for a value no block ever saw.
    /// </summary>
    public sealed class ServiceProviderDriveException : InvalidOperationException
    {
        /// <summary>No stand-in was created under that handler name on this host generation.</summary>
        public const string ReasonUnknownHandler = "unknownHandler";

        /// <summary>The wired network carries no such service-provider / service / contract endpoint.</summary>
        public const string ReasonUnknownContract = "unknownContract";

        /// <summary>Stable reason code: <c>unknownHandler</c> or <c>unknownContract</c>.</summary>
        public string Reason { get; }

        /// <summary>The addressed endpoint, rendered as <c>provider/service/contract</c>.</summary>
        public string Contract { get; }

        public ServiceProviderDriveException(string reason, string contract, string message) : base(message)
        {
            Reason = reason;
            Contract = contract;
        }
    }
}