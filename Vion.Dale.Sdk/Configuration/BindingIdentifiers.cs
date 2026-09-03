using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Configuration
{
    /// <summary>
    ///     The rule both declarative binders apply to the identifier they are about to mint for an endpoint.
    ///     An endpoint's identifier is what a topology names, what the runtime routes to, and the part of the
    ///     cloud's translation key that stands for the endpoint — so a blank one names nothing and a repeated
    ///     one names two things.
    ///     <para>
    ///         Both checks live here rather than in each binder because contract bindings and interface
    ///         bindings mint identifiers by different rules and must refuse by the same one. The refusal is at
    ///         bind time, so <c>dotnet pack</c> and a starting block both report it — an artifact carrying only
    ///         one of two declared endpoints is the shape that used to ship silently.
    ///     </para>
    /// </summary>
    internal static class BindingIdentifiers
    {
        /// <summary>
        ///     Records <paramref name="identifier" /> as minted by <paramref name="memberName" />, refusing a
        ///     blank identifier and one already minted by another member of the same block.
        /// </summary>
        public static void Claim(IDictionary<string, string> mintedBy, string identifier, string memberName, string kind, Type logicBlockType)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new InvalidOperationException($"{kind} '{memberName}' on logic block '{logicBlockType.FullName}' declares a blank Identifier. " +
                                                    "An endpoint identifier is what a topology names and what the cloud keys this endpoint's " +
                                                    "translations by, so it must not be empty or whitespace. Remove the Identifier to take the " +
                                                    "declaration's own name, or give it a value.");
            }

            if (mintedBy.TryGetValue(identifier, out var firstMember))
            {
                throw new InvalidOperationException($"{kind}s '{firstMember}' and '{memberName}' on logic block '{logicBlockType.FullName}' both resolve to the " +
                                                    $"identifier '{identifier}'. Identifiers address one endpoint each, so only one of the two would " +
                                                    "reach the introspection document while the block bound both. Give each its own Identifier.");
            }

            mintedBy[identifier] = memberName;
        }
    }
}
