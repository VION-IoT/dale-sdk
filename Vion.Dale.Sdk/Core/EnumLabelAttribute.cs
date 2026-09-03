using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Display label for an enum member. Surfaces in the dashboard via
    ///     <c>presentation.enumLabels</c>.
    /// </summary>
    /// <remarks>
    ///     Labels are translatable in the cloud, keyed by the enum's <b>short type name</b> (the namespace
    ///     is not part of the key) and the <b>C# member name</b> — renaming either orphans the
    ///     translations. Every member is cataloged, labeled or not; an unlabeled member is translatable
    ///     too, with its raw member name as the source string. See <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumLabelAttribute : Attribute
    {
        /// <summary>The label shown for this member. Translatable (see the remarks on this attribute).</summary>
        public string Label { get; }

        public EnumLabelAttribute(string label)
        {
            Label = label;
        }
    }
}