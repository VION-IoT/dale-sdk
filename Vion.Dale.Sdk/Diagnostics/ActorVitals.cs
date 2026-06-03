using System;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     A point-in-time snapshot of one actor's runtime vitals, produced by
    ///     <see cref="RuntimeVitals.Snapshot" />.
    /// </summary>
    public sealed record ActorVitals(
        string ActorName,
        ActorIdentity? Identity,
        long MessagesHandled,
        long Errors,
        TimeSpan HandlerDurationMax,
        int MailboxDepth,
        DateTimeOffset LastActivityUtc);
}
