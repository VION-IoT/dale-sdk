using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Provides display metadata for a service property or measuring point.
    ///     DisplayName takes precedence over DefaultName from ServicePropertyAttribute.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class DisplayAttribute : Attribute
    {
        public string? Name { get; }

        public string? Group { get; }

        public int Order { get; }

        public Dictionary<string, object> Annotations
        {
            get
            {
                var annotations = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(Name))
                {
                    annotations["DisplayName"] = Name;
                }

                if (!string.IsNullOrEmpty(Group))
                {
                    annotations["Group"] = Group;
                }

                if (Order >= 0)
                {
                    annotations["Order"] = Order;
                }

                return annotations;
            }
        }

        public DisplayAttribute(string? name = null, string? group = null, int order = -1)
        {
            Name = name;
            Group = group;
            Order = order;
        }
    }
}