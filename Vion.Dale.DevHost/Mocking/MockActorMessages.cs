using System.Text.Json;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    /// <summary>
    ///     Drives a service-provider value contract from a scenario / the control surface: the
    ///     generic <see cref="ServiceProviderContractHandler" /> builds the exact closed contract message from
    ///     <paramref name="Value" /> via its <c>[ScenarioWire]</c> codec and forwards it to every logic block
    ///     mapped to <paramref name="Contract" />. Replaces the contract-specific
    ///     <c>MockSet{Digital,Analog}InputMessage</c>.
    /// </summary>
    public readonly record struct MockSetServiceProviderInputMessage(ServiceProviderContractId Contract, JsonElement Value);

    public readonly record struct MockSetServicePropertyValue(IActorReference LogicBlock, SetServicePropertyValueRequest Request);

    public readonly record struct MockPublishAllStatesMessage;

    /// <summary>
    ///     Asks a value handler to answer once it has handled everything already queued for it. Start sends it
    ///     after the start acknowledgement, so the answer means every value a block published while starting
    ///     has been cached.
    /// </summary>
    internal readonly record struct StartPublicationsDrainedRequest;

    internal readonly record struct StartPublicationsDrainedResponse;
}