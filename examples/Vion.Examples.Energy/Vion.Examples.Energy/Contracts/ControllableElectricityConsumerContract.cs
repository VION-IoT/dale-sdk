using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    [Contract(BetweenInterface = "IControllableElectricityConsumer",
              AndInterface = "IControllableElectricityConsumerManager",
              BetweenDefaultName = "Verbraucher",
              AndDefaultName = "Energiemanager",
              Direction = ContractDirection.AndToBetween)]
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