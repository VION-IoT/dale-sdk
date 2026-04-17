using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Describe a service property on a service interface or logic block property
    ///     The optional parameters are used as annotations in service description
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServicePropertyAttribute : Attribute
    {
        public string? DefaultName { get; }

        public string? Unit { get; }

        public double? MinValue { get; }

        public double? MaxValue { get; }

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

                if (MinValue.HasValue)
                {
                    annotations[nameof(MinValue)] = MinValue.Value;
                }

                if (MaxValue.HasValue)
                {
                    annotations[nameof(MaxValue)] = MaxValue.Value;
                }

                return annotations;
            }
        }

        public ServicePropertyAttribute(string? defaultName = null, string? unit = null, double minValue = double.NaN, double maxValue = double.NaN)
        {
            DefaultName = defaultName;
            Unit = unit;
            MinValue = double.IsNaN(minValue) ? null : minValue;
            MaxValue = double.IsNaN(maxValue) ? null : maxValue;
        }
    }
}