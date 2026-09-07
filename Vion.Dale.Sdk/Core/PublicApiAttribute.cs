using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a type as part of the documented public API.
    ///     Types with this attribute are included in auto-generated API reference documentation
    ///     and must have XML documentation comments.
    ///     <para>
    ///         The mark belongs to the declaration carrying it and is not inherited: a subclass of a marked
    ///         type is unmarked until it declares its own. That is what <c>DALE014</c> and the manifest
    ///         generator read, so reflection has to read the same thing.
    ///     </para>
    /// </summary>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Delegate, Inherited = false)]
    public class PublicApiAttribute : Attribute
    {
    }
}