using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a <see cref="ServicePropertyAttribute" /> as an <b>instantiation parameter</b>: a
    ///     config-time scalar the operator picks in the Logic Editor when the block instance is added,
    ///     the value an <see cref="IncludedWhenAttribute" /> gate reads to decide which members the
    ///     configured instance actually has. A modifier on a real <c>[ServiceProperty]</c> (dual
    ///     annotation, like <c>[ServiceProperty]</c> + <c>[ServiceMeasuringPoint]</c>): the value is
    ///     chosen at configuration time, stored on the instance, applied to the block <b>before</b>
    ///     <c>Configure</c> on every (re)instantiation, and is <b>never settable at runtime</b>.
    ///     Parameters are independently useful as fixed setup scalars (e.g. a register base offset) —
    ///     inclusion gates are just their most important consumer.
    ///     <para>
    ///         <b>Lifecycle availability.</b> The value is applied between actor construction and the
    ///         <c>Configure</c> call, so it is set and readable everywhere except the constructor:
    ///     </para>
    ///     <list type="table">
    ///         <listheader>
    ///             <term>Site</term>
    ///             <description>Value seen</description>
    ///         </listheader>
    ///         <item>
    ///             <term>Constructor</term>
    ///             <description><b>C# initializer default only</b> — never branch on a parameter here.</description>
    ///         </item>
    ///         <item>
    ///             <term><c>Configure</c> (binding — where gates evaluate)</term>
    ///             <description>Operator value (or the C# default when none was supplied).</description>
    ///         </item>
    ///         <item>
    ///             <term><c>Ready()</c>, persistence restore, <c>Starting()</c>, timers, later hooks</term>
    ///             <description>Operator value — per-model defaults are ordinary C# here, no platform machinery.</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Placement.</b> On a property of the logic-block class itself (own or a shared base
    ///         class), root service only — never on a component type and never via a service interface.
    ///         Must be paired with <see cref="ServicePropertyAttribute" />. Enforced by DALE044.
    ///     </para>
    ///     <para>
    ///         <b>Types.</b> Discrete scalars only — <c>bool</c>, <c>enum</c>, the integer kinds, and
    ///         <c>string</c> (the same set inclusion predicates may reference). No <c>double</c>/
    ///         <c>float</c> (analog values must not drive structure), no structs/arrays, and never
    ///         <see cref="ServicePropertyAttribute.WriteOnly" /> (a secret cannot be an editor-visible
    ///         driver).
    ///     </para>
    ///     <para>
    ///         <b>Runtime immutability &amp; honesty.</b> The attribute forces the top-level
    ///         <c>schema.readOnly</c> wire flag, so the dashboard renders it read-only and every
    ///         SetPropertyValue is rejected — the operator changes the value by editing the config and
    ///         re-activating (which recycles the block), not at runtime. The property <b>must be an
    ///         auto-property</b> (no computed getter, so block code provably reads the value the gates
    ///         evaluated) and block code must not assign it outside the constructor / object initializer;
    ///         DALE044 enforces both regardless of accessor shape. <c>{ get; init; }</c> is the
    ///         recommended shape (the compiler then backstops the analyzer globally), but is not the
    ///         mechanism — reflection application by the SDK works with both <c>set</c> and <c>init</c>.
    ///     </para>
    ///     <para>
    ///         <b>Persistence.</b> Parameters are excluded from persistence auto-discovery — the config
    ///         channel is their only source of truth, so a stale gateway-persisted value can never
    ///         clobber the config-applied one after the gates already resolved.
    ///     </para>
    ///     <para>
    ///         See <see cref="IncludedWhenAttribute" /> for the gate that consumes these values, and
    ///         <c>docs/predicates.md</c> (vion-contracts) for the predicate dialect.
    ///     </para>
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class InstantiationParameterAttribute : Attribute
    {
    }
}
