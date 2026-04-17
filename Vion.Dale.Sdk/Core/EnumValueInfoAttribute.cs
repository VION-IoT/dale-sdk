using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Provides display metadata for an individual enum value.
    ///     Can be extended with additional properties as needed (e.g. descriptions, tags).
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumValueInfoAttribute : Attribute
    {
        public string? DefaultName { get; }

        public EnumValueInfoAttribute(string? defaultName = null)
        {
            DefaultName = defaultName;
        }
    }
}