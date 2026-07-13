using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Gating.Contracts
{
    /// <summary>
    ///     A request/response link between a charge controller and a charge point. The controller (source,
    ///     <c>IChargeController</c>) sets a current limit; each charge point (sink, <c>IChargePoint</c>) applies
    ///     it and answers with its state. The source generator emits the <c>IChargeController</c> /
    ///     <c>IChargePoint</c> interfaces from the role names below.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "IChargeController",
                        AndInterface = "IChargePoint",
                        BetweenDefaultName = "Charge controller",
                        AndDefaultName = "Charge point",
                        Direction = ContractDirection.Bidirectional)]
    public static class ChargeLink
    {
        [RequestResponse(From = "IChargeController", To = "IChargePoint", ResponseType = typeof(ChargeState))]
        public readonly record struct SetCurrentLimit(double Amps);

        public readonly record struct ChargeState(bool Charging, double Amps);
    }
}