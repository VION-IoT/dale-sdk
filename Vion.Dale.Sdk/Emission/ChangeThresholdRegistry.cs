using System;
using System.Collections.Concurrent;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Resolves a non-generic <see cref="IChangeThresholdAdapter" /> for a service-element value
    ///     type. Pre-registers the built-in numeric and <see cref="TimeSpan" /> thresholds and allows
    ///     libraries to register a custom <see cref="IChangeThreshold{T}" /> for their own value types.
    ///     Thread-safe; the registry is process-wide.
    /// </summary>
    internal static class ChangeThresholdRegistry
    {
        private static readonly ConcurrentDictionary<Type, IChangeThresholdAdapter> Adapters = new();

        static ChangeThresholdRegistry()
        {
            Register(new DoubleChangeThreshold());
            Register(new FloatChangeThreshold());
            Register(new DecimalChangeThreshold());
            Register(new Int32ChangeThreshold());
            Register(new Int64ChangeThreshold());
            Register(new TimeSpanChangeThreshold());
        }

        /// <summary>
        ///     Registers (or replaces) the change threshold for value type <typeparamref name="T" />.
        /// </summary>
        public static void Register<T>(IChangeThreshold<T> threshold)
        {
            if (threshold == null)
            {
                throw new ArgumentNullException(nameof(threshold));
            }

            Adapters[typeof(T)] = new ChangeThresholdAdapter<T>(threshold);
        }

        /// <summary>
        ///     Looks up the adapter for <paramref name="valueType" />. Returns <see langword="false" />
        ///     (with a null <paramref name="adapter" />) when no threshold is registered for the type.
        /// </summary>
        public static bool TryResolve(Type valueType, out IChangeThresholdAdapter adapter)
        {
            if (valueType == null)
            {
                throw new ArgumentNullException(nameof(valueType));
            }

            return Adapters.TryGetValue(valueType, out adapter!);
        }
    }
}