using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces
{
    [Contract(BetweenInterface = "IToggler", AndInterface = "IToggleable")]
    public static class Toggling
    {
        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct TogglePressed;

        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct ToggleReleased;
    }
}