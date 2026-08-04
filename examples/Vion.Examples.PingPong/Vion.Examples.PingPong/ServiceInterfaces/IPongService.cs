using Vion.Dale.Sdk.Core;

namespace Vion.Examples.PingPong.ServiceInterfaces
{
    [ServiceInterface]
    public interface IPongService
    {
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int PongsPerSecond { get; }
    }
}