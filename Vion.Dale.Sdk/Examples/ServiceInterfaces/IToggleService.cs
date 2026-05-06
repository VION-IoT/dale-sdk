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
        [ServiceProperty(Title = "Mindestkontaktdauer", Unit = "ms")]
        public int SustainDelayInMs { get; set; }

        /// <summary>
        ///     Measuring point example
        /// </summary>
        [ServiceMeasuringPoint(Title = "Anzahl Auslösungen", Unit = "count")]
        public int TimesToggled { get; }
    }
}
