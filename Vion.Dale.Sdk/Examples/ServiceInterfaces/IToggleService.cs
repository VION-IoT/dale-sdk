using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;

namespace Vion.Dale.Sdk.Examples.ServiceInterfaces
{
    [ServiceInterface]
    [ServiceRelation("LightToToggle", ServiceRelationDirection.Inwards, typeof(IToggler))]
    public interface IToggleService
    {
        /// <summary>
        ///     Property example
        /// </summary>
        [ServiceProperty("Mindestkontaktdauer", "ms")]
        public int SustainDelayInMs { get; set; }

        /// <summary>
        ///     Measuring point example
        /// </summary>
        [ServiceMeasuringPoint("Anzahl Auslösungen", "count")]
        public int TimesToggled { get; }
    }
}