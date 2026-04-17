using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    [Contract(BetweenInterface = "IObservableElectricityConsumer",
              AndInterface = "IObservableElectricityConsumerManager",
              BetweenDefaultName = "Verbraucher",
              AndDefaultName = "Energiemanager",
              Direction = ContractDirection.AndToBetween)]
    public static class ObservableElectricityConsumerContract
    {
        [RequestResponse(From = "IObservableElectricityConsumerManager", To = "IObservableElectricityConsumer", ResponseType = typeof(DataResponse))]
        public readonly record struct DataRequest;

        public readonly record struct DataResponse(DateTime Timestamp, double ActivePowerConsuming, double EnergyConsumedTotal);

        [StateUpdate(From = "IObservableElectricityConsumer", To = "IObservableElectricityConsumerManager")]
        public readonly record struct GridEffectStateUpdate(double ActivePowerGridEffect);
    }
}