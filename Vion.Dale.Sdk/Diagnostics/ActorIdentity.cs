using System;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>Whether an actor is a logic block or part of the runtime itself.</summary>
    public enum ActorCategory
    {
        LogicBlock,
        Runtime
    }

    /// <summary>
    ///     The identity dimensions of an actor, resolved at spawn time. For a logic block this is its class
    ///     (<see cref="Type" />) and originating package (<see cref="Library" />); for a runtime actor it is
    ///     the transport/handler class name with no library.
    /// </summary>
    public sealed record ActorIdentity(ActorCategory Category, string Type, string? Library)
    {
        /// <summary>
        ///     Derives an identity from an actor's receiver type and registered name. Names produced by
        ///     <see cref="LogicBlockUtils.CreateLogicBlockName" /> (the <c>logicblock_</c> prefix) are logic
        ///     blocks and carry their assembly as the library; everything else is a runtime actor.
        /// </summary>
        public static ActorIdentity For(Type receiverType, string actorName)
        {
            if (actorName.StartsWith(LogicBlockUtils.LogicBlockPrefix, StringComparison.Ordinal))
            {
                return new ActorIdentity(ActorCategory.LogicBlock, receiverType.Name, receiverType.Assembly.GetName().Name);
            }

            return new ActorIdentity(ActorCategory.Runtime, receiverType.Name, Library: null);
        }
    }
}
