using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Binds a LogicBlock property to a hardware service-provider function
    ///     (HAL: IAnalogOutput, IDigitalOutput, IModbusClient, …). The property type is the
    ///     hardware contract; the attribute carries the identity / link-multiplicity
    ///     metadata for the binding. Structurally the matched twin of
    ///     <see cref="LogicBlockInterfaceBindingAttribute" /> — distinct only because
    ///     the two are consumed by different binders (in-process actor link vs MQTT
    ///     service-provider adapter).
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ServiceProviderContractBindingAttribute : Attribute
    {
        public string? Identifier { get; init; }

        public string? DefaultName { get; init; }

        /// <summary>
        ///     Consumer-side link multiplicity for this contract binding. Default
        ///     <see cref="LinkMultiplicity.ZeroOrMore" /> (unconstrained — preserves
        ///     the pre-multiplicity behaviour). Declared only; enforced downstream.
        /// </summary>
        public LinkMultiplicity Multiplicity { get; init; } = LinkMultiplicity.ZeroOrMore;

        public string[] Tags { get; init; } = Array.Empty<string>();
    }
}
