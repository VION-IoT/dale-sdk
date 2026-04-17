using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares the UI importance of a service property or measuring point.
    ///     Primary/Secondary values are shown on dashboard tiles.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ImportanceAttribute : Attribute
    {
        public Importance Importance { get; }

        public Dictionary<string, object> Annotations
        {
            get =>
                new()
                {
                    ["Importance"] = Importance.ToString(),
                };
        }

        public ImportanceAttribute(Importance importance)
        {
            Importance = importance;
        }
    }
}