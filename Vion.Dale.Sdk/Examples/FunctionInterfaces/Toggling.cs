using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces
{
    // The [ServiceRelation] is the doc example for contract-carried relations: one declaration on the
    // contract, both halves
    // derived per bound endpoint. IToggleable is the outwards side because it is the subordinate /
    // providing one (the thing being toggled); IToggler is the inwards, driving side.
    [LogicBlockContract(BetweenInterface = "IToggler", AndInterface = "IToggleable")]
    [ServiceRelation(RelationType = "LightToToggle", OutwardsInterface = "IToggleable")]
    public static class Toggling
    {
        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct TogglePressed;

        [StateUpdate(From = "IToggler", To = "IToggleable")]
        public readonly record struct ToggleReleased;
    }
}