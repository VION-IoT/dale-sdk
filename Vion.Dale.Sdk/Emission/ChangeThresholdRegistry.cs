using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

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

        // AssemblyLoadContext.Assemblies exists on the net10.0 runtime but not in the netstandard2.0
        // compile-time facade the SDK builds against — reach it reflectively (resolved once).
        private static readonly PropertyInfo? AssembliesProperty = typeof(AssemblyLoadContext).GetProperty("Assemblies");

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
        ///     assembly that declares the throttled property) and the other SDK-referencing assemblies loaded
        ///     alongside it. This mirrors the DALE034 analyzer, which accepts a <c>MinChange</c> on a custom
        ///     type as soon as an <c>IChangeThreshold&lt;T&gt;</c> is visible anywhere in the consumer's
        ///     compilation closure — so a passing compile and a working runtime deadband agree, including when
        ///     the threshold lives in a shared foundation library.
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

            if (probeAssembly != null)
            {
                foreach (var assembly in ProbeAssemblies(probeAssembly))
                {
                    if (TryScanAndRegister(valueType, assembly, out adapter))
                    {
                        return true;
                    }
                }
            }

            adapter = null!;
            return false;
        }

        /// <summary>
        ///     The assemblies to scan for a custom threshold: the assembly that declares the property (the
        ///     common, co-located case) first, then the other SDK-referencing assemblies loaded in the same
        ///     <see cref="AssemblyLoadContext" />. The latter covers a shared foundation library that declares
        ///     the <see cref="IChangeThreshold{T}" /> while the <c>MinChange</c> knob is declared in a
        ///     different, referencing assembly — the runtime mirror of the DALE034 analyzer's cross-assembly
        ///     visibility. Using the <em>declaring</em> assembly's load context (not the SDK's) is what makes
        ///     this resolve correctly inside a plugin context, where the consumer's assemblies are isolated.
        /// </summary>
        private static IEnumerable<Assembly> ProbeAssemblies(Assembly declaring)
        {
            yield return declaring;

            var context = TryGetLoadContext(declaring);
            if (context == null)
            {
                yield break;
            }

            var sdkName = typeof(IChangeThreshold<>).Assembly.GetName().Name;
            foreach (var sibling in SiblingAssemblies(context))
            {
                // Only an assembly that references the SDK can declare an IChangeThreshold<T>; skip framework
                // and unrelated assemblies so the scan stays cheap.
                if (!ReferenceEquals(sibling, declaring) && ReferencesSdk(sibling, sdkName))
                {
                    yield return sibling;
                }
            }
        }

        private static AssemblyLoadContext? TryGetLoadContext(Assembly assembly)
        {
            try
            {
                return AssemblyLoadContext.GetLoadContext(assembly);
            }
            catch
            {
                return null;
            }
        }

        private static Assembly[] SiblingAssemblies(AssemblyLoadContext context)
        {
            try
            {
                if (AssembliesProperty?.GetValue(context) is IEnumerable<Assembly> assemblies)
                {
                    // Snapshot: the set can change while enumerating (assemblies loading concurrently); a
                    // best-effort view is enough — a co-located threshold is already covered by the declaring
                    // assembly.
                    return assemblies.ToArray();
                }
            }
            catch
            {
                // ignored — fall through to the empty set
            }

            return Array.Empty<Assembly>();
        }

        private static bool ReferencesSdk(Assembly assembly, string? sdkName)
        {
            if (sdkName == null)
            {
                return false;
            }

            try
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    if (string.Equals(reference.Name, sdkName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Dynamic / reflection-only assemblies may refuse GetReferencedAssemblies — skip them.
            }

            return false;
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