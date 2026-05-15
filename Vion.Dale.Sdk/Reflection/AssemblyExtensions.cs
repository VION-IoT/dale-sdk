using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Reflection
{
    public static class AssemblyExtensions
    {
        extension(Assembly[] assemblies)
        {
            /// <summary>
            ///     Retrieves all concrete (non-interface, non-abstract) types that implement or inherit from the specified type.
            /// </summary>
            /// <param name="derivedFrom">The interface or base type to find implementors/inheritors of.</param>
            /// <returns>A list of concrete types that implement or inherit from <paramref name="derivedFrom" />.</returns>
            /// <remarks>
            ///     If the same type name exists in multiple assemblies, only the type from the assembly with the highest version is
            ///     returned.
            /// </remarks>
            /// <exception cref="AssemblyTypeLoadException">Thrown when an assembly references unresolvable dependencies.</exception>
            public List<Type> GetConcreteTypes(Type derivedFrom)
            {
                return assemblies.GetConcreteTypesInternal(derivedFrom).ToList();
            }

            /// <summary>
            ///     Retrieves the first concrete (non-interface, non-abstract) type that implements or inherits from the specified
            ///     type.
            /// </summary>
            /// <param name="derivedFrom">The interface or base type to find an implementor/inheritor of.</param>
            /// <returns>The first concrete type that implements or inherits from <paramref name="derivedFrom" />.</returns>
            /// <remarks>
            ///     If the same type name exists in multiple assemblies, only the type from the assembly with the highest version is
            ///     returned.
            /// </remarks>
            /// <exception cref="AssemblyTypeLoadException">Thrown when an assembly references unresolvable dependencies.</exception>
            /// <exception cref="InvalidOperationException">Thrown when no matching type is found.</exception>
            public Type GetConcreteType(Type derivedFrom)
            {
                return assemblies.GetConcreteTypesInternal(derivedFrom).First();
            }

            private IEnumerable<Type> GetConcreteTypesInternal(Type derivedFrom)
            {
                var derivedFromAssemblyName = derivedFrom.Assembly.GetName().Name;

                return assemblies.Where(assembly => AssemblyCouldContainType(assembly, derivedFrom, derivedFromAssemblyName))
                                 .SelectMany(assembly =>
                                             {
                                                 try
                                                 {
                                                     return assembly.GetTypes();
                                                 }
                                                 catch (ReflectionTypeLoadException exception)
                                                 {
                                                     throw new AssemblyTypeLoadException(assembly, derivedFrom, exception);
                                                 }
                                             })
                                 .Where(type => derivedFrom.IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
                                 .GroupBy(type => type.Name)
                                 .Select(group => group.OrderByDescending(t => t.Assembly.GetName().Version).First());
            }

            /// <summary>
            ///     Determines if an assembly could potentially contain types derived from the specified type.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         This method filters assemblies before calling <see cref="Assembly.GetTypes" /> to avoid scanning
            ///         assemblies that cannot contain the desired type and to prevent <see cref="ReflectionTypeLoadException" />.
            ///     </para>
            ///     <para>
            ///         <b>Why filtering is necessary:</b><br />
            ///         Some assemblies (e.g., Metalama.Patterns.Observability) reference compile-time-only dependencies like
            ///         Microsoft.CodeAnalysis.CSharp.
            ///         These dependencies are excluded from runtime output.
            ///         Calling <see cref="Assembly.GetTypes" /> on such assemblies throws a <see cref="ReflectionTypeLoadException" />
            ///         because it attempts to load the missing dependencies.
            ///     </para>
            ///     <para>
            ///         <b>How filtering works:</b><br />
            ///         An assembly can only contain types derived from <paramref name="derivedFrom" /> if it references
            ///         the assembly where <paramref name="derivedFrom" /> is defined.
            ///         For example, when searching for implementations of <see cref="IServiceProviderHandlerActor" /> (defined in
            ///         Vion.Dale.Sdk),
            ///         only assemblies that reference Vion.Dale.Sdk are scanned.
            ///         This naturally excludes problematic assemblies like Metalama.Patterns.Observability which don't reference
            ///         Vion.Dale.Sdk.
            ///     </para>
            ///     <para>
            ///         <b>Practical benefit:</b><br />
            ///         This approach limits scanning to assemblies we directly control (e.g., Vion.Dale.Sdk.DigitalIo) or assemblies
            ///         our customers control (e.g., their LogicBlock libraries). This drastically reduces the chance of encountering
            ///         a <see cref="ReflectionTypeLoadException" />, and if one does occur, it can be easily fixed by either us or
            ///         our customers since they control the problematic assembly.
            ///     </para>
            /// </remarks>
            private static bool AssemblyCouldContainType(Assembly assembly, Type derivedFrom, string derivedFromAssemblyName)
            {
                return assembly == derivedFrom.Assembly || assembly.GetReferencedAssemblies().Any(assemblyName => assemblyName.Name == derivedFromAssemblyName);
            }
        }
    }

    public class AssemblyTypeLoadException : Exception
    {
        public Assembly Assembly { get; }

        public Type DerivedFrom { get; }

        public AssemblyTypeLoadException(Assembly assembly, Type derivedFrom, ReflectionTypeLoadException innerException) :
            base($"Failed to search for implementations of '{derivedFrom.FullName}'. " +
                 $"Loading types from assembly '{assembly.GetName().Name}' failed due to unresolvable dependencies.",
                 innerException)
        {
            Assembly = assembly;
            DerivedFrom = derivedFrom;
        }
    }
}
