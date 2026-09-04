using System;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     The development host's real-time safety budgets — the backstops that bound a wait no clock mode can
    ///     complete. Each defaults to the value the host has always used; a caller overrides one with
    ///     <see cref="DevHostBuilder.WithSafetyBudgets" /> (or, for <see cref="Quiescence" />,
    ///     <see cref="DevHostBuilder.WithDeterministicStepping" />).
    ///     <para>
    ///         They are budgets, not tolerances: the normal path completes in milliseconds and never approaches
    ///         one. They exist so a genuinely stuck host surfaces as a named failure instead of a hang — and so
    ///         a test can reach that failure without waiting out the production value.
    ///     </para>
    /// </summary>
    public sealed record DevHostBudgets
    {
        /// <summary>
        ///     How long a service-property write waits for the block's own acknowledgement before completing
        ///     regardless. A write that consumes it was never applied — the swallowed-exception hollow ack.
        /// </summary>
        public TimeSpan WriteAcknowledgement { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        ///     The wall-clock bound on waiting for every block to acknowledge start. The wait itself is routed
        ///     through the registered clock, so on a stepped host nothing would ever advance its due-time; this
        ///     is the only thing in the start sequence no clock mode can stall.
        /// </summary>
        public TimeSpan StartAcknowledgement { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     The wall-clock bound on the whole teardown sequence. Its own steps wait through the registered
        ///     clock — the same reason the start acknowledgement needs a backstop — so this is the only thing in
        ///     a teardown no clock mode can stall. Generous by design: the normal path completes on the
        ///     acknowledgements in milliseconds and a slow machine must never trip it.
        /// </summary>
        public TimeSpan StopSequence { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        ///     The wall-clock ceiling on one quiescence wait during deterministic stepping. A system that never
        ///     settles surfaces as a thrown failure naming the predicate rather than as an infinite wait.
        /// </summary>
        public TimeSpan Quiescence { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>Throws when any budget is not a positive span — a zero or negative backstop is no backstop.</summary>
        public void Validate()
        {
            Require(WriteAcknowledgement, nameof(WriteAcknowledgement));
            Require(StartAcknowledgement, nameof(StartAcknowledgement));
            Require(StopSequence, nameof(StopSequence));
            Require(Quiescence, nameof(Quiescence));
        }

        private static void Require(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name, value, "A development host safety budget must be a positive span.");
            }
        }
    }
}
