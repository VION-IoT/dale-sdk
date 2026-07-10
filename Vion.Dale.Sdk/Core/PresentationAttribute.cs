using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     UI-side presentation hints for a service property, measuring point, or method.
    ///     Routes into the per-property <c>presentation</c> sibling document. Open for
    ///     preset inheritance — integrators subclass to ship their own domain vocabulary.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class PresentationAttribute : Attribute
    {
        // ── Naming ──

        /// <summary>
        ///     Override the displayed label. Falls back to schema.title (primitives) or the
        ///     C# property name. For enum-/struct-typed properties (where schema.title is
        ///     identity-bearing), this is the only way to set a UI label distinct from the
        ///     CLR type name.
        /// </summary>
        public string? DisplayName { get; init; }

        // ── Layout ──

        /// <summary>
        ///     Group key. The dashboard renders all properties with the same Group key in
        ///     one section. Well-known keys are constants in <see cref="PropertyGroup" />;
        ///     integrators may supply their own string keys (e.g. "acme.powertrain") which
        ///     the dashboard renders as a generic section with the raw key as the header.
        ///     Section order is set by [LogicBlock(Groups = ...)]; default order is the
        ///     platform-defined order. Within-group order is by <see cref="Order" />.
        /// </summary>
        public string? Group { get; init; }

        /// <summary>
        ///     Sort hint within a group. Ascending; properties without an explicit value
        ///     sort between explicit values, stable-by-default (base-class first, declaration
        ///     order within each class). Used for finer ordering than the group level.
        ///     <see cref="int.MinValue" /> means "unset" (attribute parameter types can't be nullable;
        ///     <c>PropertyMetadataBuilder</c> converts the sentinel to null in the
        ///     codec-side <c>Presentation.Order</c>).
        /// </summary>
        public int Order { get; init; } = int.MinValue;

        /// <summary>
        ///     Tile composition rank. Primary/Secondary surface on the auto-generated
        ///     LogicBlock tile; Normal renders in detail views only; Hidden suppresses
        ///     the property entirely.
        /// </summary>
        public Importance Importance { get; init; } = Importance.Normal;

        // ── Roles ──

        /// <summary>
        ///     Marks this property as an operational status indicator for the LogicBlock.
        ///     A block can carry multiple — distinct status dimensions (e.g. operating mode
        ///     + connection state + activity status). Must be enum-typed (or nullable enum).
        ///     Per-member severity comes from [Severity]; per-member display labels from
        ///     [EnumLabel].
        /// </summary>
        public bool StatusIndicator { get; init; }

        // ── Formatting ──

        /// <summary>
        ///     Display precision for numeric values. <see cref="int.MinValue" /> (default)
        ///     means "unset" and uses sensible per-type defaults (attribute parameter types
        ///     can't be nullable; <c>PropertyMetadataBuilder</c> converts the
        ///     sentinel to null in the codec-side <c>Presentation.Decimals</c>).
        ///     Ignored for non-numeric schemas (analyzer warning DALE021).
        /// </summary>
        public int Decimals { get; init; } = int.MinValue;

        // ── Custom widget routing ──

        /// <summary>
        ///     Routing key for the dashboard's generic renderer and for custom widget templates.
        ///     Open string; unrecognized values are silently ignored. Well-known platform values
        ///     are constants in <see cref="UiHints" />. The "statusIndicator" value is
        ///     auto-emitted from <see cref="StatusIndicator" /> — don't set directly.
        /// </summary>
        public string? UiHint { get; init; }

        /// <summary>
        ///     Format-token string for date / duration / numeric renderers. Type-orthogonal —
        ///     a separate concern from <see cref="UiHint" /> (which selects the widget) and
        ///     <see cref="Decimals" /> (which controls numeric precision). The renderer
        ///     (dashboard / DevHost) consumes the value as a moment.js / day.js compatible
        ///     format-token string when the property's CLR type is
        ///     <see cref="System.DateTime" /> or <see cref="System.TimeSpan" />.
        ///     <para />
        ///     Two reserved sentinel values short-circuit the token interpreter:
        ///     <list type="bullet">
        ///         <item><c>"relative"</c> → auto-updating "3 minutes ago"-style date display</item>
        ///         <item><c>"humanize"</c> → humanized duration like "3 hours"</item>
        ///     </list>
        ///     Common tokens (see <see cref="Formats" /> for shortcuts):
        ///     <list type="bullet">
        ///         <item><c>"LLLL"</c> → "Wednesday, May 13, 2026 2:32 PM" (locale full + weekday)</item>
        ///         <item><c>"LLL"</c> → "May 13, 2026 2:32 PM" (locale long)</item>
        ///         <item><c>"YYYY-MM-DD HH:mm:ss"</c> → "2026-05-13 14:32:05"</item>
        ///         <item><c>"YYYY-MM-DD HH:mm:ss.SSS"</c> → with millisecond precision</item>
        ///         <item><c>"HH:mm:ss"</c> → "01:23:45" (typical for durations)</item>
        ///     </list>
        ///     Token reference: <see href="https://momentjs.com/docs/#/displaying/format/" />.
        /// </summary>
        public string? Format { get; init; }

        // ── Conditional visibility ──

        /// <summary>
        ///     Conditional-visibility predicate. When set, this member is hidden from the dashboard
        ///     form whenever the predicate evaluates false, and shown otherwise. Evaluated
        ///     <b>reactively in the UI only</b>, against the
        ///     <b>
        ///         live values of sibling service
        ///         properties of the same logic-block instance
        ///     </b>
        ///     . The member keeps existing and
        ///     functioning everywhere else — runtime, MQTT, cloud DB, introspection — so this is a
        ///     pure <b>display</b> hint, never an existence or behavior gate (contrast RFC 0016's
        ///     <c>[ExistsWhen]</c>, which removes the member across every layer). A property with no
        ///     <c>VisibleWhen</c> is always shown (backward compatible). Evaluation is
        ///     <b>fail-open</b>: a broken or unresolvable predicate shows the member.
        ///     <para />
        ///     <b>Grammar (in brief).</b> A typed subset of the dashboard's widget-expression dialect:
        ///     <list type="bullet">
        ///         <item>
        ///             comparisons — <c>==</c> <c>!=</c> (bool / enum / string / integer refs),
        ///             <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c> <c>&gt;=</c> (integer refs only);
        ///         </item>
        ///         <item>membership — <c>ref in [literal, …]</c> (enum / string / integer, homogeneous list);</item>
        ///         <item>
        ///             boolean combinators — <c>&amp;&amp;</c> <c>||</c> <c>!</c> and parentheses;
        ///             a <b>bare</b> ref must be <c>bool</c>; <c>!</c> applies only to a bool ref or a
        ///             parenthesized predicate (write <c>!(A == 5)</c>, not <c>!A == 5</c>);
        ///         </item>
        ///         <item>
        ///             literals — integers (int32 range), <c>true</c> / <c>false</c>, and strings.
        ///             <b>Enum members are quoted strings</b> (<c>Mode == 'Eco'</c>). Single quotes are
        ///             the recommended style (no escaping inside a C# attribute string).
        ///         </item>
        ///     </list>
        ///     Refs sit on the <b>left</b> of every comparison (no Yoda conditions). Arithmetic, the
        ///     ternary, function calls, and pipes are rejected by the analyzer. The canonical grammar
        ///     and semantics live in the shared <c>docs/predicates.md</c> (vion-contracts), pinned by
        ///     <c>Predicates/predicate-conformance.json</c>.
        ///     <para />
        ///     <b>Reference scope and addressing.</b> Referenced targets must be <c>[ServiceProperty]</c>s
        ///     of type <c>bool</c>, <c>enum</c>, integer, or <c>string</c> (never <c>double</c>/<c>float</c>,
        ///     <c>WriteOnly</c>, or measuring-point-only members):
        ///     <list type="bullet">
        ///         <item>
        ///             <b>Bare ref</b> (<c>DirectMeasurement</c>) — a property on the <b>same service</b>
        ///             as the annotated member.
        ///         </item>
        ///         <item>
        ///             <b>Qualified ref</b> (<c>Service.Property</c>) — a property on a
        ///             <b>
        ///                 sibling
        ///                 service of the same logic-block instance
        ///             </b>
        ///             .
        ///         </item>
        ///     </list>
        ///     Service identifiers are those the introspection uses (invisible in source, so spelled out
        ///     here): the <b>component-service</b> identifier is the <b>holding property's name</b>
        ///     (a <c>ChargingPoint ChargingPoint1 { get; }</c> property forms service
        ///     <c>ChargingPoint1</c>), and the <b>root-service</b> identifier is the
        ///     <b>logic-block class name</b> (address the root's own <c>IsExternallyLocked</c> as
        ///     <c>MyBlockClassName.IsExternallyLocked</c>). Renaming the class therefore breaks
        ///     root-qualified predicates in the same compilation — the analyzer (DALE041/DALE042)
        ///     catches it at build. <c>nameof()</c> concatenation is supported:
        ///     <c>VisibleWhen = nameof(DirectMeasurement) + " == false"</c>.
        /// </summary>
        public string? VisibleWhen { get; init; }
    }
}