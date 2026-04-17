using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Provides a widget hint for the UI to render a specialized control.
    ///     Examples: "battery-gauge", "color-picker", "slider"
    /// </summary>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class UIHintAttribute : Attribute
    {
        public string Widget { get; }

        public Dictionary<string, object> Annotations
        {
            get =>
                new()
                {
                    ["UIHint"] = Widget,
                };
        }

        public UIHintAttribute(string widget)
        {
            Widget = widget;
        }
    }
}