using System;
using System.Globalization;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Validation
{
    /// <summary>
    ///     The range a Modbus timeout may be configured in, and the one guard both transports apply to it.
    /// </summary>
    /// <remarks>
    ///     The upper bound is the framework timer's, not a protocol limit: a longer duration is refused by the
    ///     cancellation source every operation arms, and refusing it where the value is accepted is what keeps a
    ///     mistyped duration from being reported later as a transport fault that closes the socket. Sharing the
    ///     guard is deliberate — the two transports drifted apart the last time each carried its own.
    /// </remarks>
    [PublicApi]
    public static class ModbusTimeoutLimits
    {
        /// <summary>
        ///     The longest a connect timeout, an operation timeout or a per-call timeout may be — just under 25 days.
        ///     A value at the bound is accepted; it is the largest delay a cancellation source carries on every
        ///     runtime a plugin built against this SDK can be loaded into.
        /// </summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        /// <summary>
        ///     Refuses a timeout outside <c>(0, <see cref="MaxTimeout" />]</c>.
        /// </summary>
        /// <param name="timeout">The duration to check.</param>
        /// <param name="subject">The property or parameter the duration was given for, named in the exception.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="timeout" /> is zero or negative, or longer than <see cref="MaxTimeout" />.
        /// </exception>
        public static void Validate(TimeSpan timeout, string subject)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(subject, timeout, string.Format(CultureInfo.InvariantCulture, "{0} must be greater than zero.", subject));
            }

            if (timeout > MaxTimeout)
            {
                throw new ArgumentOutOfRangeException(subject, timeout, string.Format(CultureInfo.InvariantCulture, "{0} must not exceed {1}.", subject, MaxTimeout));
            }
        }
    }
}