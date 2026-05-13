using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Binds a LogicBlock property to a hardware service-provider function
    ///     (HAL: IAnalogOutput, IDigitalOutput, IModbusClient, …). The property type is the
    ///     hardware contract; the attribute carries the identity / sharing / cardinality
    ///     metadata for the binding.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ServiceProviderContractBindingAttribute : Attribute
    {
        public string? Identifier { get; init; }

        public string? DefaultName { get; init; }

        public CardinalityType Cardinality { get; init; } = CardinalityType.Mandatory;

        public SharingType Sharing { get; init; } = SharingType.Shared;

        public string[] Tags { get; init; } = Array.Empty<string>();
    }
}
