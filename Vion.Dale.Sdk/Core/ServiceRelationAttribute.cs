using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares that an operator wiring of this contract constitutes a service relation of the given
    ///     type between the two blocks' services (RFC 0019). The SDK derives one relation half per bound
    ///     interface endpoint on each side; a relation row materialises in the cloud only where an
    ///     operator-authored interface mapping connects two blocks over this contract. Relations are pure
    ///     topology metadata — they carry no runtime behaviour.
    ///     <para>
    ///         <b>Direction convention:</b> <see cref="OutwardsInterface" /> names the subordinate
    ///         (providing) side — the start of the arrow, e.g. a consumer, supplier, meter, or cascade
    ///         child. The contract's other interface is the inwards, aggregating (managing) side — the end
    ///         of the arrow, e.g. an energy manager or cascade parent.
    ///     </para>
    ///     <para>
    ///         The half attaches to the service that owns the endpoint: the root service for a
    ///         class-implemented interface, and the component service for a property-bound one. A component
    ///         whose type carries no service surface (no <see cref="ServiceInterfaceAttribute" />, no
    ///         <see cref="ServicePropertyAttribute" /> / <see cref="ServiceMeasuringPointAttribute" />
    ///         members) has no node in the cloud graph, so its endpoint emits <b>no</b> half — it still
    ///         binds and wires normally. Give such a component one service property to let it participate,
    ///         or implement the contract interface class-level for a block-granularity edge. Analyzer
    ///         DALE045 warns at the property.
    ///     </para>
    ///     <para>
    ///         <b>Package skew:</b> halves are derived per package at parse time from the contract assembly
    ///         that package was built against. For a contract shared across libraries, both sides' packages
    ///         must be built against a contract version carrying the declaration; skew degrades to "no row",
    ///         never a failure.
    ///     </para>
    /// </summary>
    /// <example>
    ///     <code>
    /// [LogicBlockContract(BetweenInterface = "IControllableConsumer", AndInterface = "IControllableConsumerManager")]
    /// [ServiceRelation(RelationType = "LinkedEnergyManagerConsumer", OutwardsInterface = "IControllableConsumer")]
    /// public static class ControllableConsumerContract { }
    /// </code>
    /// </example>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ServiceRelationAttribute : Attribute
    {
        /// <summary>
        ///     Identifier of the relation as it appears in the cloud API (<c>relationType</c>). An opaque,
        ///     ordinal-compared, stable contract string — renaming it is a breaking metadata change for
        ///     every dashboard / API consumer keying on it. Must be unique across the declarations on one
        ///     contract (analyzer DALE045).
        /// </summary>
        public required string RelationType { get; init; }

        /// <summary>
        ///     Which of the contract's two interfaces (<see cref="LogicBlockContractAttribute.BetweenInterface" />
        ///     / <see cref="LogicBlockContractAttribute.AndInterface" />, by the same short-name string) is
        ///     the outwards — subordinate / providing — side. Must equal one of the two; validated at bind
        ///     time and by analyzer DALE045.
        /// </summary>
        public required string OutwardsInterface { get; init; }
    }

    /// <summary>
    ///     Which side of a service relation a derived half represents. The split key between the
    ///     <c>inwardRelations</c> and <c>outwardRelations</c> arrays on the wire; the value itself never
    ///     reaches the wire.
    ///     <para>
    ///         Per the RFC 0019 convention, <see cref="Outwards" /> is the subordinate / providing side
    ///         (start of the arrow) named by <see cref="ServiceRelationAttribute.OutwardsInterface" />, and
    ///         <see cref="Inwards" /> is the aggregating / managing side (end of the arrow).
    ///     </para>
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