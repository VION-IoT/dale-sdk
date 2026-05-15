using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    [InternalApi]
    public readonly record struct LogicBlockContractId(LogicBlockId LogicBlockId, string ContractIdentifier)
    {
        public override string ToString()
        {
            return $"{LogicBlockId}_{ContractIdentifier}";
        }
    }
}