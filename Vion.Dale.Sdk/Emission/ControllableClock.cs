using System;
using System.Reflection;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The SDK's own copy of the controllable-clock probe. A controllable (FakeTimeProvider-style)
    ///     clock exposes a public instance <c>Advance(TimeSpan)</c> returning <c>void</c>. This mirrors
    ///     the structural detection in <c>Vion.Dale.ProtoActor</c> (ActorContext / ActorSystem) so the
    ///     shipped SDK needs no compile-time reference to the test-only TimeProvider.Testing assembly,
    ///     and cannot reference <c>Vion.Dale.ProtoActor</c> either.
    /// </summary>
    internal static class ControllableClock
    {
        public static bool Detect(TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                return false;
            }

            var advance = timeProvider.GetType().GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(TimeSpan) }, null);

            return advance is { ReturnType: { } returnType } && returnType == typeof(void);
        }
    }
}