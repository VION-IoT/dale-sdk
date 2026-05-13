using Vion.Dale.Sdk.Core;
using Vion.Examples.PingPong.Contracts;

namespace Vion.Examples.PingPong.ServiceInterfaces
{
    [ServiceInterface]
    [ServiceRelation("PingPong", ServiceRelationDirection.Inwards, typeof(IPong))]
    public interface IPongService
    {
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int PongsPerSecond { get; }
    }
}