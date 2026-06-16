using System;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     A point-in-time snapshot of one actor's runtime vitals, produced by
    ///     <see cref="RuntimeVitals.Snapshot" />. Counts (<see cref="MessagesPosted" />,
    ///     <see cref="MessagesHandled" />, <see cref="Errors" />) and <see cref="HandlerDurationTotal" /> are
    ///     cumulative since spawn (the backend derives rate, and mean = total / handled); the <c>*Max</c>
    ///     values are over a recent window so they reflect current behaviour rather than a lifetime
    ///     high-water mark; <see cref="MailboxDepth" /> is instantaneous.
    ///     <para>
    ///         <see cref="MessagesPosted" /> counts every message ever enqueued to this actor's mailbox (fed
    ///         by the Proto mailbox-statistics hook, so it includes system and infrastructure messages, not
    ///         just user-handler ones); <see cref="MailboxDepth" /> is the live <c>posted − received</c>
    ///         backlog of messages still queued (not yet dequeued). Note that <c>MessagesPosted</c> and
    ///         <see cref="MessagesHandled" /> sit on different denominators — handled is fed by the actor
    ///         middleware and counts only user-handler completions — so their difference does NOT return to
    ///         zero at idle; <see cref="MailboxDepth" /> is the signal to use for "is the mailbox empty".
    ///     </para>
    /// </summary>
    public sealed record ActorVitals(
        string ActorName,
        ActorIdentity? Identity,
        long MessagesPosted,
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