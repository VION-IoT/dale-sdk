using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Display label for an enum member. Surfaces in the dashboard via
    ///     <c>presentation.enumLabels</c>.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumLabelAttribute : Attribute
    {
        public string Label { get; }

        public EnumLabelAttribute(string label)
        {
            Label = label;
        }
    }
}
