using Vion.Dale.Sdk.Core;

namespace Vion.Examples.PingPong.Contracts
{
    // One [ServiceRelation] on the contract is the whole declaration: every block implementing IPing or
    // IPong takes part automatically, on whichever side it implements. IPong is the outwards side because
    // it is the responding, providing one — note that this is independent of ContractDirection below,
    // which describes how messages flow, not who is subordinate to whom.
    [LogicBlockContract(BetweenInterface = "IPing",
                        AndInterface = "IPong",
                        BetweenDefaultName = "Ping-Sender",
                        AndDefaultName = "Pong-Empfänger",
                        Direction = ContractDirection.Bidirectional)]
    [ServiceRelation(RelationType = "PingPong", OutwardsInterface = "IPong")]
    public static class PingPong
    {
        [RequestResponse(From = "IPing", To = "IPong", ResponseType = typeof(PongResponse))]
        public readonly record struct PingRequest;

        public readonly record struct PongResponse;
    }
}