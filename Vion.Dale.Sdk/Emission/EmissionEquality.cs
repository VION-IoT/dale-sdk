using System.Collections;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The comparison behind the RFC 0004 value-equality floor (<see cref="Throttler" /> step 1) and the
    ///     stop-drain's redundant-emit check. Value equality for scalars, <c>string</c>, enums and flat
    ///     <c>readonly record struct</c>s; <em>content</em> equality for <c>ImmutableArray&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <c>ImmutableArray&lt;T&gt;</c> is the only collection shape a service element may use (DALE008 bans
    ///     <c>T[]</c>, <c>List&lt;T&gt;</c> and friends), but it implements
    ///     <c>IEquatable&lt;ImmutableArray&lt;T&gt;&gt;</c> as <em>reference equality of the underlying array</em>
    ///     — so a plain <c>Equals</c> reports a rebuilt-but-identical table as changed, and the floor can never
    ///     fire for it. A per-row table rebuilt each control cycle would then republish forever, carrying no
    ///     news; RFC 0004 names <c>ImmutableArray</c> as one of the shapes the floor exists to cover, so the
    ///     floor compares content.
    ///     <para>
    ///         <see cref="IStructuralEquatable" /> is the framework's own compare-me-by-content contract:
    ///         <c>ImmutableArray&lt;T&gt;</c> implements it as a length check plus an element walk, and
    ///         <see cref="StructuralComparisons.StructuralEqualityComparer" /> recurses, so a nested
    ///         <c>ImmutableArray&lt;ImmutableArray&lt;T&gt;&gt;</c> compares by content too. Element types are
    ///         closed and all carry correct value equality (DALE003 scalars; DALE016 flat
    ///         <c>readonly record struct</c>), so the element walk has no reference-equality holes left.
    ///     </para>
    ///     <para>
    ///         Cost: the walk only runs to completion when the values are <em>equal</em> — exactly when it saves
    ///         a serialize plus a retained publish of the whole table. A changed value short-circuits at the
    ///         first differing element.
    ///     </para>
    /// </remarks>
    internal static class EmissionEquality
    {
        /// <summary>
        ///     <see langword="true" /> when <paramref name="candidate" /> carries no news relative to
        ///     <paramref name="lastEmitted" />. A <see langword="null" /> on either side alone is a difference.
        /// </summary>
        public static bool AreEqual(object? lastEmitted, object? candidate)
        {
            // The null cases (and the type-mismatch case) fall through to Equals, which handles them without
            // the structural walk having to special-case them.
            if (lastEmitted is IStructuralEquatable structural && candidate != null)
            {
                return structural.Equals(candidate, StructuralComparisons.StructuralEqualityComparer);
            }

            return Equals(lastEmitted, candidate);
        }
    }
}