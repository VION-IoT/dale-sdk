using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    [InternalApi]
    public static class LogicBlockUtils
    {
        public const string LogicBlockPrefix = "logicblock_";

        public static string CreateLogicBlockName(string logicBlockIdentifier, string logicBlockId)
        {
            return $"{LogicBlockPrefix}{logicBlockIdentifier}_{logicBlockId}";
        }
    }
}