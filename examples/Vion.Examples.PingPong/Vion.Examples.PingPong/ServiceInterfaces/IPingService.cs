using Vion.Dale.Sdk.Core;

namespace Vion.Examples.PingPong.ServiceInterfaces
{
    // No relation declaration here: the relation rides on the PingPong contract, and the SDK derives this
    // service's half from the IPing interface the Ping block implements.
    [ServiceInterface]
    public interface IPingService
    {
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int PingsPerSecond { get; }

        [ServiceProperty]
        public bool Pause { get; set; }
    }
}