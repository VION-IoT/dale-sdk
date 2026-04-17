using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Classifies a service property or measuring point into a semantic category.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class CategoryAttribute : Attribute
    {
        public PropertyCategory Category { get; }

        public Dictionary<string, object> Annotations
        {
            get =>
                new()
                {
                    ["Category"] = Category.ToString(),
                };
        }

        public CategoryAttribute(PropertyCategory category)
        {
            Category = category;
        }
    }
}