using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces
{
    /// <summary>
    ///     Example contract
    /// </summary>
    [Contract(BetweenInterface = EnergyController, AndInterface = EnergyConsumer)]
    public static class EnergyControlContract
    {
        private const string EnergyController = "IEnergyController";

        private const string EnergyConsumer = "IEnergyConsumer";

        [RequestResponse(From = EnergyController, To = EnergyConsumer, ResponseType = typeof(EnergyConsumerDataResponse))]
        public readonly record struct EnergyConsumerDataRequest(int RequestId);

        public readonly record struct EnergyConsumerDataResponse(int RequestId, double CurrentPowerConsumption);

        [Command(From = EnergyController, To = EnergyConsumer)]
        public readonly record struct EnergyConsumerControlCommand(double SetPowerLimit);

        [StateUpdate(From = EnergyConsumer, To = EnergyController)]
        public readonly record struct EnergyConsumerConfigurationStateUpdate(double CurrentPowerLimit, bool IsActive);
    }
}