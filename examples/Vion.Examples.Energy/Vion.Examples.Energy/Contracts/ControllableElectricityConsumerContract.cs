using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    // The consumer is the outwards side: it is the subordinate, providing end of the edge, and the manager
    // is the aggregating end. Declaring it here is enough for every implementer of either interface —
    // including nested components: ChargingStationMultiPointSimulation's two ChargingPoint properties are
    // services in their own right, so each contributes its own edge rather than one lumped at block level.
    [LogicBlockContract(BetweenInterface = "IControllableElectricityConsumer",
                        AndInterface = "IControllableElectricityConsumerManager",
                        BetweenDefaultName = "Verbraucher",
                        AndDefaultName = "Energiemanager",
                        Direction = ContractDirection.AndToBetween)]
    [ServiceRelation(RelationType = "LinkedControllableConsumer", OutwardsInterface = "IControllableElectricityConsumer")]
    public static class ControllableElectricityConsumerContract
    {
        [RequestResponse(From = "IControllableElectricityConsumerManager", To = "IControllableElectricityConsumer", ResponseType = typeof(DataResponse))]
        public readonly record struct DataRequest;

        public readonly record struct DataResponse(DateTime Timestamp, double ActivePowerConsuming, double EnergyConsumedTotal);

        [StateUpdate(From = "IControllableElectricityConsumer", To = "IControllableElectricityConsumerManager")]
        public readonly record struct StateUpdate(double RequestedActivePower);

        [Command(From = "IControllableElectricityConsumerManager", To = "IControllableElectricityConsumer")]
        public readonly record struct Command(double AllocatedActivePower);

        [StateUpdate(From = "IControllableElectricityConsumer", To = "IControllableElectricityConsumerManager")]
        public readonly record struct GridEffectStateUpdate(double ActivePowerGridEffect);
    }
}