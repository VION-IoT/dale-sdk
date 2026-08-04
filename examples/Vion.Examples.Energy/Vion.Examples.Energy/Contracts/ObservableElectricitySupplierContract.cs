using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    [LogicBlockContract(BetweenInterface = "IObservableElectricitySupplier",
                        AndInterface = "IObservableElectricitySupplierManager",
                        BetweenDefaultName = "Erzeuger",
                        AndDefaultName = "Energiemanager",
                        Direction = ContractDirection.AndToBetween)]
    [ServiceRelation(RelationType = "LinkedObservableSupplier", OutwardsInterface = "IObservableElectricitySupplier")]
    public static class ObservableElectricitySupplierContract
    {
        [RequestResponse(From = "IObservableElectricitySupplierManager", To = "IObservableElectricitySupplier", ResponseType = typeof(DataResponse))]
        public readonly record struct DataRequest;

        public readonly record struct DataResponse(DateTime Timestamp, double ActivePowerSupplying, double EnergySuppliedTotal);

        [StateUpdate(From = "IObservableElectricitySupplier", To = "IObservableElectricitySupplierManager")]
        public readonly record struct StateUpdate(double PeakActivePower);

        [StateUpdate(From = "IObservableElectricitySupplier", To = "IObservableElectricitySupplierManager")]
        public readonly record struct GridEffectStateUpdate(double ActivePowerGridEffect);
    }
}