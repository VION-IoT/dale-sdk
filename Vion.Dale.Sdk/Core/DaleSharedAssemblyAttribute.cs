using System;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks an assembly as shared across all plugins in the Dale runtime.
    ///     When multiple plugins reference the same assembly (e.g. a contract extension library),
    ///     the Dale plugin loader ensures only one copy is loaded and all plugins share the same
    ///     instance. This prevents type identity conflicts in cross-plugin actor message routing.
    /// </summary>
    /// <remarks>
    ///     Apply this attribute at the assembly level in any library that defines:
    ///     <list type="bullet">
    ///         <item>Contract handler actors (implementations of <see cref="IServiceProviderHandlerActor" />)</item>
    ///         <item>Contract message types used in cross-plugin communication</item>
    ///     </list>
    ///     <para>
    ///         Without this attribute, each plugin loads its own copy of the assembly, causing
    ///         types like <c>ContractMessage&lt;SetDigitalOutput&gt;</c> to have different type
    ///         identities across plugins. This breaks actor message pattern matching and routing.
    ///     </para>
    ///     <para>
    ///         Example usage:
    ///         <code>[assembly: DaleSharedAssembly]</code>
    ///     </para>
    /// </remarks>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class DaleSharedAssemblyAttribute : Attribute;
}