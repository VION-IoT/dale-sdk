using Vion.Dale.Sdk.Core;

namespace Vion.Examples.ToggleLight.Contracts
{
    [Contract(BetweenInterface = "IToggler",
              AndInterface = "IToggleable",
              BetweenDefaultName = "Signalgeber",
              AndDefaultName = "Signalempfänger",
              Direction = ContractDirection.BetweenToAnd)]
    public static class Toggling
    {
        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct TogglePressed;

        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct ToggleReleased;
    }
}