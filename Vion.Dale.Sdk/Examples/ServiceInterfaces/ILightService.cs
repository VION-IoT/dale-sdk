using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;

namespace Vion.Dale.Sdk.Examples.ServiceInterfaces
{
    [ServiceInterface]
    [ServiceRelation("LightToToggle", ServiceRelationDirection.Outwards, typeof(IToggleable))]
    public interface ILightService
    {
        /// <summary>
        ///     Writable property example
        /// </summary>
        [ServiceProperty]
        public bool OnOff { get; set; }

        /// <summary>
        ///     Read-only property example, also a measuring point
        /// </summary>
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public bool IsOn { get; }

        /// <summary>
        ///     Read-only property example with non-trivial type
        /// </summary>
        [ServiceProperty]
        public DateTime LastSwitchedOn { get; }

        /// <summary>
        ///     Measuring point example with non-trivial type
        /// </summary>
        [ServiceMeasuringPoint]
        public TimeSpan TotalTimeOn { get; }

        /// <summary>
        ///     Property with unit example
        /// </summary>
        [ServiceProperty(Title = "Leistung", Unit = "W", Minimum = 0)]
        public double NominalPower { get; }
    }
}
