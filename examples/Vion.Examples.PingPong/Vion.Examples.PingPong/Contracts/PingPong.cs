using Vion.Dale.Sdk.Core;

namespace Vion.Examples.PingPong.Contracts
{
    [Contract(BetweenInterface = "IPing",
              AndInterface = "IPong",
              BetweenDefaultName = "Ping-Sender",
              AndDefaultName = "Pong-Empfänger",
              Direction = ContractDirection.Bidirectional)]
    public static class PingPong
    {
        [RequestResponse(From = "IPing", To = "IPong", ResponseType = typeof(PongResponse))]
        public readonly record struct PingRequest;

        public readonly record struct PongResponse;
    }
}