using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    /// <summary>
    ///     Uniquely identifies a logic block interface by combining the logic block ID and interface identifier.
    /// </summary>
    [InternalApi]
    public readonly record struct InterfaceId(LogicBlockId LogicBlockId, string InterfaceIdentifier)
    {
        // ToString override to return a string representation for logging etc.
        public override string ToString()
        {
            return $"{LogicBlockId}_{InterfaceIdentifier}";
        }
    }
}