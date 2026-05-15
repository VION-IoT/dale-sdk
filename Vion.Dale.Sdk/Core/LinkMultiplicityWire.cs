using System;
using Vion.Contracts.Conventions;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Maps the SDK-owned <see cref="LinkMultiplicity" /> to the shared
    ///     vion-contracts wire token. The token strings live in
    ///     <see cref="LogicBlockWiringConventions" /> so the dale-parser producer and
    ///     the (future) cloud-api consumer agree on the exact wire vocabulary —
    ///     mirroring how <c>WriteOnlyConventions</c> owns its sentinel string without
    ///     owning the SDK attribute.
    /// </summary>
    internal static class LinkMultiplicityWire
    {
        public static string ToToken(LinkMultiplicity multiplicity)
        {
            return multiplicity switch
            {
                LinkMultiplicity.ExactlyOne => LogicBlockWiringConventions.ExactlyOne,
                LinkMultiplicity.ZeroOrOne => LogicBlockWiringConventions.ZeroOrOne,
                LinkMultiplicity.OneOrMore => LogicBlockWiringConventions.OneOrMore,
                LinkMultiplicity.ZeroOrMore => LogicBlockWiringConventions.ZeroOrMore,
                _ => throw new ArgumentOutOfRangeException(nameof(multiplicity), multiplicity, null),
            };
        }
    }
}
