using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Metadata for an implementation of a logic-block interface. Applies to a class
    ///     (when the LB implements the interface directly) OR a property (when the property's
    ///     value implements the interface, e.g. an inner ChargingPoint instance). Both cases
    ///     are "metadata for an existing interface relationship".
    ///
    ///     AllowMultiple = true to handle properties whose type implements multiple interfaces
    ///     (each <see cref="LogicBlockInterfaceBindingAttribute" /> targets one interface via
    ///     <see cref="ForInterface" />).
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class,
                    AllowMultiple = true, Inherited = true)]
    public class LogicBlockInterfaceBindingAttribute : Attribute
    {
        /// <summary>The interface this binding metadata applies to.</summary>
        public Type ForInterface { get; }

        public string? Identifier { get; init; }

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
