using Vion.Dale.Sdk.Core;
using Vion.Examples.PingPong.Contracts;

namespace Vion.Examples.PingPong.ServiceInterfaces
{
    [ServiceInterface]
    [ServiceRelation("PingPong", ServiceRelationDirection.Outwards, typeof(IPing))]
    public interface IPingService
    {
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int PingsPerSecond { get; }

        [ServiceProperty]
        public bool Pause { get; set; }
    }
}