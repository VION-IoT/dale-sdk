using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    /// <summary>
    ///     Uniquely identifies a logic block instance within the actor system.
    /// </summary>
    [InternalApi]
    public readonly record struct LogicBlockId(string Id)
    {
        // Implicit conversion: LogicBlockId → string
        public static implicit operator string(LogicBlockId logicBlockId)
        {
            return logicBlockId.Id;
        }

        // Implicit conversion: string → LogicBlockId
        public static implicit operator LogicBlockId(string id)
        {
            return new LogicBlockId(id);
        }

        // ToString override string interpolation, logging, etc.
        public override string ToString()
        {
            return Id;
        }
    }
}