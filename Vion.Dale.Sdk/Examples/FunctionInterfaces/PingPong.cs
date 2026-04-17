using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces
{
    [Contract(BetweenInterface = "IPing", AndInterface = "IPong")]
    public static class PingPong
    {
        [RequestResponse(From = "IPing", To = "IPong", ResponseType = typeof(PongResponse))]
        public readonly record struct PingRequest;

        public readonly record struct PongResponse;
    }
}