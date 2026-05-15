using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Mocking
{
    public readonly record struct MockSetDigitalInputMessage(string ServiceProviderIdentifier, string ServiceIdentifier, string ContractIdentifier, bool Value);

    public readonly record struct MockSetAnalogInputMessage(string ServiceProviderIdentifier, string ServiceIdentifier, string ContractIdentifier, double Value);

    public readonly record struct MockSetServicePropertyValue(IActorReference LogicBlock, SetServicePropertyValueRequest Request);

    public readonly record struct MockPublishAllStatesMessage;
}