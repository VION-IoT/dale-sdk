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
    ///     <see cref="DefaultName" /> — and the role names the endpoint's contract declares via
    ///     <see cref="LogicBlockContractAttribute.BetweenDefaultName" /> /
    ///     <see cref="LogicBlockContractAttribute.AndDefaultName" /> — are translatable in the cloud, keyed
    ///     by the block's full type name and this binding's <see cref="Identifier" />. Since the identifier
    ///     defaults to a C# name (<c>{PropertyName}_{InterfaceName}</c> for a property-bound endpoint, the
    ///     bare interface name for a class-implemented one), renaming the property, the interface or the
    ///     class normally mints a new key and orphans the translations authored against the old one —
    ///     setting <see cref="Identifier" /> explicitly is what decouples the two. See
    ///     <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
    public class LogicBlockInterfaceBindingAttribute : Attribute
    {
        /// <summary>The interface this binding metadata applies to.</summary>
        public Type ForInterface { get; }

        /// <summary>
        ///     Stable identifier for this interface binding. Defaults to <c>{PropertyName}_{InterfaceName}</c>
        ///     (property-bound) or the bare interface name (class-implemented). Pin it to rename the
        ///     property, interface or class without changing the introspected identity (see the remarks on
        ///     this attribute) — it is the only knob that decouples the two.
        /// </summary>
        public string? Identifier { get; init; }

        /// <summary>
        ///     Human-readable name for the interface endpoint. Translatable (see the remarks on this
        ///     attribute); the value here is the source default and the permanent fallback.
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