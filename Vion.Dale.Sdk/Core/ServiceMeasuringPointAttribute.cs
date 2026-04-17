using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Define a measuring point on a Service interface or logic block property.
    ///     The optional parameters are used as annotations in service description
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceMeasuringPointAttribute : Attribute
    {
        public string? DefaultName { get; }

        public string? Unit { get; }

        public Dictionary<string, object> Annotations
        {
            get
            {
                var annotations = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(DefaultName))
                {
                    annotations[nameof(DefaultName)] = DefaultName;
                }

                if (!string.IsNullOrEmpty(Unit))
                {
                    annotations[nameof(Unit)] = Unit;
                }

                return annotations;
            }
        }

        public ServiceMeasuringPointAttribute(string? defaultName = null, string? unit = null)
        {
            DefaultName = defaultName;
            Unit = unit;
        }
    }
}