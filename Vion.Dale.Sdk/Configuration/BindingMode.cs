namespace Vion.Dale.Sdk.Configuration
{
    /// <summary>
    ///     Selects how the declarative binders treat <see cref="Vion.Dale.Sdk.Core.IncludedWhenAttribute" />
    ///     inclusion gates (RFC 0016 config-time structural gating).
    /// </summary>
    public enum BindingMode
    {
        /// <summary>
        ///     Bind the <b>full maximum</b> member set regardless of gates, and record each gated member's
        ///     predicate into the emitted binding metadata / <c>ServiceInfo.IncludedWhen</c>. Used only by
        ///     <see cref="Vion.Dale.Sdk.Introspection.LogicBlockIntrospection" />, which runs a default
        ///     instance and must produce a configuration-independent definition view.
        /// </summary>
        Definition,

        /// <summary>
        ///     Evaluate each member's <c>[IncludedWhen]</c> predicate against the block's current
        ///     <see cref="Vion.Dale.Sdk.Core.InstantiationParameterAttribute" /> values (already applied
        ///     pre-<c>Configure</c>) and <b>skip</b> members whose gate is false — never bound, never
        ///     constructed, never registered. The default for the runtime, DevHost, and TestKit.
        ///     Evaluation is strict / fail-closed: a parse or evaluation error fails <c>Configure</c>.
        /// </summary>
        Live,
    }
}