using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declare a timer method that should be called at regular intervals.
    ///     If the identifier is not set, the method name is used.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Method)]
    public class TimerAttribute : Attribute
    {
        public string? Identifier { get; }

        public double IntervalSeconds { get; }

        public TimerAttribute(double intervalSeconds, string? identifier = null)
        {
            if (intervalSeconds <= 0)
            {
                throw new ArgumentException("Timer interval must be greater than zero", nameof(intervalSeconds));
            }

            IntervalSeconds = intervalSeconds;
            Identifier = identifier;
        }
    }
}