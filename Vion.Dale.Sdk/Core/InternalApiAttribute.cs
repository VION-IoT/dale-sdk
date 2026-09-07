using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a public type as intentionally excluded from the documented public API.
    ///     Use this to suppress DALE014 warnings on types that must be public for technical
    ///     reasons (e.g. Metalama fabrics, DI extensions) but are not part of the user-facing SDK.
    ///     <para>
    ///         The mark belongs to the declaration carrying it and is not inherited: a subclass of a marked
    ///         type is unmarked until it declares its own.
    ///     </para>
    /// </summary>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Delegate, Inherited = false)]
    public class InternalApiAttribute : Attribute
    {
    }
}