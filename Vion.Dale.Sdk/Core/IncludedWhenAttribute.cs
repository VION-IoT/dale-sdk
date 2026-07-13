using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Gates whether a member is part of the configured instance, based on the instance's
    ///     <see cref="InstantiationParameterAttribute" /> values chosen at configuration time. The C#
    ///     member always exists in code; what the gate controls is whether it is <b>discoverable,
    ///     wireable, bound, and published</b> for a given instance. When the predicate is false the
    ///     member is skipped entirely at bind time — never bound, never constructed (for contracts),
    ///     never registered, no MQTT topic, no cloud read-model row, no editor slot. As if it were
    ///     never declared. A member with no gate is unconditional (fully backward compatible).
    ///     <para>
    ///         <b>This is a hard, config-time existence gate — not display.</b> Contrast
    ///         <see cref="PresentationAttribute.VisibleWhen" />, which merely hides a still-existing,
    ///         still-functioning member in the dashboard. Use <c>[IncludedWhen]</c> when the choice
    ///         changes <i>what the editor can wire or what topics exist</i>; use <c>VisibleWhen</c> when
    ///         the block merely behaves differently.
    ///     </para>
    ///     <para>
    ///         <b>Predicate.</b> The shared dialect (<c>docs/predicates.md</c>, vion-contracts), with
    ///         references restricted to <see cref="InstantiationParameterAttribute" /> properties of the
    ///         <b>same block</b> (bare single-segment refs only — no <c>Service.Property</c> form).
    ///         Evaluated strict / fail-closed: an unparseable predicate or a missing/null/type-mismatched
    ///         parameter value fails <c>Configure</c> and the block reports unhealthy. DALE043 rejects a
    ///         predicate that does not parse, a qualified ref, or a ref that does not resolve to an
    ///         <c>[InstantiationParameter]</c>; DALE044 rejects a type/literal mismatch.
    ///     </para>
    ///     <para>
    ///         <b>Placement matrix</b> (DALE043 enforces it; the rule is <i>gateable = what the
    ///         definition view exposes as a wireable/publishable member</i>):
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Gateable:</b> a property-based interface binding
    ///             (<c>[LogicBlockInterfaceBinding]</c> on a component implementing a
    ///             <c>[LogicInterface]</c>); a contract-binding property
    ///             (<c>[ServiceProviderContractBinding]</c>); a service-bearing component property (a type
    ///             carrying <c>[ServiceProperty]</c>/<c>[ServiceMeasuringPoint]</c> members). Gating a
    ///             component gates everything the binders derive from it — its interface binding and its
    ///             whole service (all properties + measuring points).
    ///         </item>
    ///         <item>
    ///             <b>Not gateable:</b> a scalar <c>[ServiceProperty]</c>/<c>[ServiceMeasuringPoint]</c>
    ///             (it keeps existing and publishing — use <c>VisibleWhen</c> for display relevance);
    ///             a <c>[Timer]</c> method (timers are not in the definition view — gate in code:
    ///             <c>if (ChargePointCount &lt; 3) return;</c>); a class-implemented interface (no member
    ///             to carry the attribute — use a property-based binding); the block class itself
    ///             (whole-block existence = the operator adds the instance or not).
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Null-contract hazard (read carefully).</b> An excluded <b>contract</b> property is
    ///         never constructed by the binder, so it is <b>null</b> at runtime. Declare gated contract
    ///         properties nullable (<c>IDigitalOutput?</c>) and gate your own fan-out code against the
    ///         same parameter. (An excluded <i>interface component</i> stays non-null — the author's own
    ///         <c>new()</c> — but is inert: unbound, never published, never wired.)
    ///     </para>
    ///     <para>
    ///         <b>Sibling-contract gating pattern.</b> A per-plug station-level contract declared as a
    ///         <i>sibling</i> property of the gated component must carry the <b>same predicate</b>,
    ///         author-written — the platform cannot infer the grouping. Example: a <c>Point2</c> gated
    ///         with <c>[IncludedWhen("ChargePointCount &gt;= 2")]</c> and its
    ///         <c>ChargingPoint2Output</c> contract carrying the identical gate.
    ///     </para>
    ///     <para>
    ///         Inheritance: gates declared on a shared base logic-block class apply to every leaf.
    ///         Re-declaring, changing, or removing the gate on an <c>override</c>/<c>new</c> member is
    ///         rejected (DALE043) — declare the gate once, at the declaration the hierarchy shares.
    ///     </para>
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Method)]
    public class IncludedWhenAttribute : Attribute
    {
        /// <summary>
        ///     Creates an inclusion gate over the given predicate in the shared dialect
        ///     (<c>docs/predicates.md</c>), referencing <see cref="InstantiationParameterAttribute" />
        ///     properties of the same block.
        /// </summary>
        public IncludedWhenAttribute(string predicate)
        {
            Predicate = predicate;
        }

        /// <summary>
        ///     The inclusion predicate. References <see cref="InstantiationParameterAttribute" /> scalars
        ///     of the same block; evaluated strict / fail-closed at bind time.
        /// </summary>
        public string Predicate { get; }
    }
}
