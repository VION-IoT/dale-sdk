using System;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     A point-in-time snapshot of one actor's runtime vitals, produced by
    ///     <see cref="RuntimeVitals.Snapshot" />. Counts (<see cref="MessagesHandled" />,
    ///     <see cref="Errors" />) and <see cref="HandlerDurationTotal" /> are cumulative since spawn (the
    ///     backend derives rate, and mean = total / handled); the <c>*Max</c> values are over a recent
    ///     window so they reflect current behaviour rather than a lifetime high-water mark;
    ///     <see cref="MailboxDepth" /> is instantaneous.
    /// </summary>
    public sealed record ActorVitals(
        string ActorName,
        ActorIdentity? Identity,
        long MessagesHandled,
        long Errors,
        TimeSpan HandlerDurationMax,
        TimeSpan HandlerDurationTotal,
        int MailboxDepth,
        int MailboxDepthMax,
        TimeSpan TimerCallbackDurationMax,
        TimeSpan TimerJitterMax,
        DateTimeOffset LastActivityUtc);
}