namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Defines the directional relationship between the two sides of a contract.
    /// </summary>
    [PublicApi]
    public enum ContractDirection
    {
        /// <summary>
        ///     No specific direction. No arrows between "Between" and "And". No specific parent-child relationship.
        /// </summary>
        None,

        /// <summary>
        ///     Bidirectional arrows between "Between" and "And". No specific parent-child relationship.
        /// </summary>
        Bidirectional,

        /// <summary>
        ///     Arrow from "Between" to "And". In a tree, "Between" would be the parent and "And" the child.
        /// </summary>
        BetweenToAnd,

        /// <summary>
        ///     Arrow from "And" to "Between". In a tree, "And" would be the parent and "Between" the child.
        /// </summary>
        AndToBetween,
    }
}