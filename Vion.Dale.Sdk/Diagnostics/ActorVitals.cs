using System;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     A point-in-time snapshot of one actor's runtime vitals, produced by
    ///     <see cref="RuntimeVitals.Snapshot" />.
    /// </summary>
    public sealed record ActorVitals(
        string ActorName,
        long MessagesHandled,
        long Errors,
        TimeSpan HandlerDurationMax,
        DateTimeOffset LastActivityUtc);
}
