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
    /// <remarks>
    ///     <see cref="DefaultName" /> is translatable in the cloud, keyed by the block's full type name and
    ///     this binding's <see cref="Identifier" /> — which defaults to the annotated property's name, so
    ///     renaming that property orphans the translations unless <see cref="Identifier" /> is pinned. See
    ///     <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceProviderContractBindingAttribute : Attribute
    {
        /// <summary>
        ///     Stable identifier for this contract binding. Defaults to the annotated property's name; pin
        ///     it to rename the property without changing the identifier.
        /// </summary>
        public string? Identifier { get; init; }

        /// <summary>Human-readable name for the contract. Translatable (see the remarks on this attribute).</summary>
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