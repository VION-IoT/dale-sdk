using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    /// Marks a type as part of the documented public API.
    /// Types with this attribute are included in auto-generated API reference documentation
    /// and must have XML documentation comments.
    /// </summary>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct)]
    public class PublicApiAttribute : Attribute
    {
    }
}