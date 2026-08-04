using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares that wiring this contract between two logic blocks is a service relation of the given
    ///     type — a typed edge between the two blocks' services, published as topology metadata. Declare it
    ///     once, here on the contract: every block implementing either of the contract's two interfaces
    ///     then takes part automatically, on whichever side it implements. Relations describe the graph;
    ///     they carry no runtime behaviour and change nothing about how messages flow.
    ///     <para>
    ///         <b>Which side is which.</b> <see cref="OutwardsInterface" /> names the subordinate,
    ///         providing side — the start of the arrow. The contract's other interface is the inwards,
    ///         aggregating side — the end of the arrow. A block may implement both sides; each is treated
    ///         independently.
    ///     </para>
    ///     <para>
    ///         <b>A component must be a service to take part.</b> The relation attaches to the service that
    ///         owns the interface: the block's own service when the block class implements it, or the
    ///         component's service when a property's type does. A component type with no service surface
    ///         (no <see cref="ServiceInterfaceAttribute" />, no <see cref="ServicePropertyAttribute" /> or
    ///         <see cref="ServiceMeasuringPointAttribute" /> members) is not a service, so nothing is
    ///         emitted for it — the interface still binds and wires as usual. Give the component one
    ///         service property to let it take part, or implement the interface on the block class for an
    ///         edge at block granularity.
    ///     </para>
    ///     <para>
    ///         <b>Both sides need the declaration.</b> Each package resolves relations at build time
    ///         against the contract assembly it was compiled against, so a block built against an older
    ///         contract simply contributes nothing for that pair — a missing edge, never an error.
    ///     </para>
    /// </summary>
    /// <example>
    ///     <code>
    /// [LogicBlockContract(BetweenInterface = "IHeatPump", AndInterface = "IHeatPumpManager")]
    /// [ServiceRelation(RelationType = "LinkedHeatPump", OutwardsInterface = "IHeatPump")]
    /// public static class HeatPumpContract { }
    /// </code>
    /// </example>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ServiceRelationAttribute : Attribute
    {
        /// <summary>
        ///     Names the kind of edge this relation is. Opaque and compared verbatim, and it is what both
        ///     the API and the UI key on — so treat it as a public identifier: choose it deliberately, and
        ///     expect a rename to break every consumer. Must be unique among the declarations on one
        ///     contract.
        /// </summary>
        public required string RelationType { get; init; }

        /// <summary>
        ///     Which of the contract's two interfaces is the outwards — subordinate, providing — side.
        ///     Must be spelled exactly like the corresponding
        ///     <see cref="LogicBlockContractAttribute.BetweenInterface" /> or
        ///     <see cref="LogicBlockContractAttribute.AndInterface" />; any other value is rejected.
        /// </summary>
        public required string OutwardsInterface { get; init; }
    }

    /// <summary>
    ///     Which side of a service relation a service sits on. <see cref="Outwards" /> is the side named by
    ///     <see cref="ServiceRelationAttribute.OutwardsInterface" />; the contract's other interface is
    ///     <see cref="Inwards" />.
    /// </summary>
    [PublicApi]
    public enum ServiceRelationDirection
    {
        /// <summary>
        ///     This service is the target (end) of the relation — the aggregating / managing side.
        /// </summary>
        Inwards,

        /// <summary>
        ///     This service is the source (start) of the relation — the subordinate / providing side.
        /// </summary>
        Outwards,
    }
}