using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The resolved, per-member emission policy derived from an <see cref="IThrottleConfigured" />
    ///     attribute and the member's value type. Created once at start by <c>LogicBlockBase</c> and
    ///     handed to a <see cref="Throttler" />.
    /// </summary>
    internal sealed class ThrottlePolicy
    {
        /// <summary>When set, every distinct value emits immediately (throttle + deadband bypassed).</summary>
        public bool Immediate;

        /// <summary>The raw deadband expression, or <c>null</c> when no deadband is configured.</summary>
        public string? MinChange;

        /// <summary>Parsed <c>MinInterval</c>; <see cref="TimeSpan.Zero" /> when throttling is disabled.</summary>
        public TimeSpan MinInterval;

        /// <summary>Resolved from <see cref="MinChange" /> + the value type, else <c>null</c>.</summary>
        public IChangeThresholdAdapter? Threshold;

        /// <summary><c>true</c> when <c>MinInterval</c> parsed to zero ("0"/"0ms") — leading-edge only.</summary>
        public bool ThrottleDisabled;

        public static ThrottlePolicy FromConfigured(IThrottleConfigured cfg, Type valueType, Assembly? probeAssembly = null)
        {
            if (cfg == null)
            {
                throw new ArgumentNullException(nameof(cfg));
            }

            if (valueType == null)
            {
                throw new ArgumentNullException(nameof(valueType));
            }

            var minInterval = DurationParser.Parse(cfg.MinInterval);

            IChangeThresholdAdapter? threshold = null;
            if (!string.IsNullOrEmpty(cfg.MinChange))
            {
                // Unwrap Nullable<T> so a deadband on e.g. double? resolves the double threshold, and fall
                // back to scanning the declaring assembly for a custom IChangeThreshold<T> (DF-34). Both
                // mirror the DALE034 analyzer (it unwraps Nullable and accepts an impl visible in the
                // consumer's own compilation), so a passing compile implies a working runtime deadband.
                var resolveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                if (ChangeThresholdRegistry.TryResolve(resolveType, probeAssembly, out var adapter))
                {
                    threshold = adapter;
                }
            }

            return new ThrottlePolicy
                   {
                       MinInterval = minInterval,
                       ThrottleDisabled = minInterval == TimeSpan.Zero,
                       Immediate = cfg.Immediate,
                       MinChange = cfg.MinChange,
                       Threshold = threshold,
                   };
        }
    }
}