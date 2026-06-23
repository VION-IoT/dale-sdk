using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The immutable outcome of <see cref="Throttler.Offer" />. <see cref="Deadline" /> is only
    ///     meaningful when <see cref="Action" /> is <see cref="EmitAction.Hold" />.
    /// </summary>
    internal readonly struct OfferResult
    {
        private OfferResult(EmitAction action, DateTimeOffset deadline)
        {
            Action = action;
            Deadline = deadline;
        }

        public EmitAction Action { get; }

        public DateTimeOffset Deadline { get; }

        public static OfferResult Emit { get; } = new(EmitAction.Emit, default);

        public static OfferResult Drop { get; } = new(EmitAction.Drop, default);

        public static OfferResult Hold(DateTimeOffset deadline)
        {
            return new OfferResult(EmitAction.Hold, deadline);
        }
    }
}