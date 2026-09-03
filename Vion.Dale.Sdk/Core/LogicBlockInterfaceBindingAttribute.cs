using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Metadata for an implementation of a logic-block interface. Applies to a class
    ///     (when the LB implements the interface directly) OR a property (when the property's
    ///     value implements the interface, e.g. an inner ChargingPoint instance). Both cases
    ///     are "metadata for an existing interface relationship".
    ///     AllowMultiple = true to handle properties whose type implements multiple interfaces
    ///     (each <see cref="LogicBlockInterfaceBindingAttribute" /> targets one interface via
    ///     <see cref="ForInterface" />).
    /// </summary>
    /// <remarks>
    ///     <see cref="DefaultName" /> — and the contract's role names
    ///     (<see cref="LogicBlockContractAttribute.BetweenDefaultName" /> /
    ///     <see cref="LogicBlockContractAttribute.AndDefaultName" />) — are translatable in the cloud, keyed
    ///     by the block's full type name and this binding's <see cref="Identifier" />, which defaults to a
    ///     C# name. Pin <see cref="Identifier" /> to rename the property, interface or class without
    ///     orphaning the translations. See <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
    public class LogicBlockInterfaceBindingAttribute : Attribute
    {
        /// <summary>The interface this binding metadata applies to.</summary>
        public Type ForInterface { get; }

        /// <summary>
        ///     Stable identifier for this interface binding. Defaults to <c>{PropertyName}_{InterfaceName}</c>
        ///     (property-bound) or the bare interface name (class-implemented); pin it to rename without
        ///     changing the identifier.
        /// </summary>
        public string? Identifier { get; init; }

        /// <summary>
        ///     Human-readable name for the interface endpoint. Translatable (see the remarks on this
        ///     attribute).
        /// </summary>
        public string? DefaultName { get; init; }

        public string[] Tags { get; init; } = Array.Empty<string>();

        /// <summary>
        ///     Consumer-side link multiplicity for this interface binding. Default
        ///     <see cref="LinkMultiplicity.ZeroOrMore" /> (unconstrained — preserves
        ///     the pre-multiplicity behaviour). Declared only; enforced downstream.
        /// </summary>
        public LinkMultiplicity Multiplicity { get; init; } = LinkMultiplicity.ZeroOrMore;

        public LogicBlockInterfaceBindingAttribute(Type forInterface)
        {
            ForInterface = forInterface;
        }
    }
}