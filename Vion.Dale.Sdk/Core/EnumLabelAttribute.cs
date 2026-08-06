using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Display label for an enum member. Surfaces in the dashboard via
    ///     <c>presentation.enumLabels</c>.
    /// </summary>
    /// <remarks>
    ///     Labels are translatable in the cloud, keyed by the enum's <b>short type name</b> (the wire's
    ///     <c>schema.title</c> — the namespace never travels) and the <b>C# member name</b>. Renaming the
    ///     enum type orphans the translations of every one of its members even when the members are
    ///     untouched; renaming a member orphans that member's. Cataloging is <b>exhaustive</b>: a member
    ///     with no label is translatable too, with its raw C# member name as the source string — which is
    ///     also what the dashboard renders for it. See <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumLabelAttribute : Attribute
    {
        /// <summary>
        ///     The label shown for this member. Translatable (see the remarks on this attribute); the
        ///     value here is the source default and the permanent fallback.
        /// </summary>
        public string Label { get; }

        public EnumLabelAttribute(string label)
        {
            Label = label;
        }
    }
}