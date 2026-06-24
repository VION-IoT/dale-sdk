using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        /// <summary>
        ///     Looks up the adapter for <paramref name="valueType" />, and — on a cache miss — discovers a
        ///     custom <see cref="IChangeThreshold{T}" /> by scanning <paramref name="probeAssembly" /> (the
        ///     assembly that declares the throttled property). This mirrors the DALE034 analyzer, which
        ///     accepts a <c>MinChange</c> on a custom type as soon as an <c>IChangeThreshold&lt;T&gt;</c> is
        ///     visible in the consumer's own compilation — so a passing compile and a working runtime agree.
        /// </summary>
        public static bool TryResolve(Type valueType, Assembly? probeAssembly, out IChangeThresholdAdapter adapter)
        {
            if (valueType == null)
            {
                throw new ArgumentNullException(nameof(valueType));
            }

            if (Adapters.TryGetValue(valueType, out adapter!))
            {
                return true;
            }

            return probeAssembly != null && TryScanAndRegister(valueType, probeAssembly, out adapter);
        }

        /// <summary>
        ///     Scans <paramref name="probeAssembly" /> for an instantiable type implementing
        ///     <see cref="IChangeThreshold{T}" /> closed over <paramref name="valueType" />, instantiates it
        ///     via its parameterless constructor, wraps it in a <see cref="ChangeThresholdAdapter{T}" />, and
        ///     caches it. Returns <see langword="false" /> when none is found. The first match wins and the
        ///     process-wide cache means each value type is resolved at most once.
        /// </summary>
        private static bool TryScanAndRegister(Type valueType, Assembly probeAssembly, out IChangeThresholdAdapter adapter)
        {
            var closedInterface = typeof(IChangeThreshold<>).MakeGenericType(valueType);

            foreach (var candidate in SafeGetTypes(probeAssembly))
            {
                if (candidate.IsAbstract || candidate.IsInterface || candidate.IsGenericTypeDefinition || !closedInterface.IsAssignableFrom(candidate))
                {
                    continue;
                }

                // Needs a parameterless constructor (public or not) to be instantiated at start. A
                // discovered impl without one cannot be used; skip it (resolution ultimately fails → the
                // caller fails fast rather than silently dropping the deadband).
                if (candidate.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) == null)
                {
                    continue;
                }

                var instance = Activator.CreateInstance(candidate, true);
                if (instance == null)
                {
                    continue;
                }

                var adapterType = typeof(ChangeThresholdAdapter<>).MakeGenericType(valueType);
                adapter = (IChangeThresholdAdapter)Activator.CreateInstance(adapterType, instance)!;

                // GetOrAdd so a concurrent scan for the same type observes a single shared adapter.
                adapter = Adapters.GetOrAdd(valueType, adapter);
                return true;
            }

            adapter = null!;
            return false;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A partially-loadable assembly still yields the types that did load — enough to find a
                // threshold declared alongside the block.
                return ex.Types.Where(t => t != null)!;
            }
        }
    }
}