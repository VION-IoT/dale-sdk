using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces
{
    [LogicBlockContract(BetweenInterface = EnergyBufferController, AndInterface = EnergyBuffer)]
    public static class EnergyBufferContract
    {
        private const string EnergyBufferController = "IEnergyBufferController";

        private const string EnergyBuffer = "IEnergyBuffer";

        [RequestResponse(From = EnergyBufferController, To = EnergyBuffer, ResponseType = typeof(EnergyBufferDataResponse))]
        public readonly record struct EnergyBufferDataRequest;

        public readonly record struct EnergyBufferDataResponse(double CurrentL1, double CurrentL2, double CurrentL3);

        [Command(From = EnergyBufferController, To = EnergyBuffer)]
        public readonly record struct EnergyBufferControlCommand(double AllocatedCurrentL1, double AllocatedCurrentL2, double AllocatedCurrentL3);

        [Command(From = EnergyBufferController, To = EnergyBuffer)]
        public readonly record struct EnergyBufferControlCommand2(bool OnOff);
    }
}