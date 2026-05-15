using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.ServiceInterfaces
{
    [ServiceInterface]
    public interface IOtherService
    {
        [ServiceProperty]
        public bool OtherProperty { get; set; }

        /// <summary>
        ///     Measuring point example with non-trivial type
        /// </summary>
        [ServiceMeasuringPoint]
        public double OtherMeasuringPoint { get; }
    }
}