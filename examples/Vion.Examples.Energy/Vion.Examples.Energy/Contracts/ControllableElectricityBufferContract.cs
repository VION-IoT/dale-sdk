using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    [Contract(BetweenInterface = "IControllableElectricityBuffer",
              AndInterface = "IControllableElectricityBufferManager",
              BetweenDefaultName = "Speicher",
              AndDefaultName = "Energiemanager",
              Direction = ContractDirection.AndToBetween)]
    public static class ControllableElectricityBufferContract
    {
        [RequestResponse(From = "IControllableElectricityBufferManager", To = "IControllableElectricityBuffer", ResponseType = typeof(DataResponse))]
        public readonly record struct DataRequest;

        public readonly record struct DataResponse(
            DateTime Timestamp,
            double ActivePowerCharging,
            double ActivePowerDischarging,
            double EnergyChargingTotal,
            double EnergyDischargingTotal,
            double StateOfCharge);

        [StateUpdate(From = "IControllableElectricityBuffer", To = "IControllableElectricityBufferManager")]
        public readonly record struct StateUpdate(double MaximumActivePowerCharging, double MaximumActivePowerDischarging, double Capacity);

        [Command(From = "IControllableElectricityBufferManager", To = "IControllableElectricityBuffer")]
        public readonly record struct Command(double AllocatedActivePowerCharging, double AllocatedActivePowerDischarging);

        [StateUpdate(From = "IControllableElectricityBuffer", To = "IControllableElectricityBufferManager")]
        public readonly record struct GridEffectStateUpdate(double ActivePowerGridEffect);
    }
}