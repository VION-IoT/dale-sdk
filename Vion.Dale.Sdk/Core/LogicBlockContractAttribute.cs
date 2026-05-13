using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a class as a contract container grouping messages
    ///     (<see cref="CommandAttribute" />, <see cref="StateUpdateAttribute" />,
    ///     <see cref="RequestResponseAttribute" />) exchanged between two LogicBlock
    ///     interfaces.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockContractAttribute : Attribute
    {
        public required string BetweenInterface { get; init; }

        public required string AndInterface { get; init; }

        public string? BetweenDefaultName { get; init; }

        public string? AndDefaultName { get; init; }

        public ContractDirection Direction { get; init; } = ContractDirection.None;
    }
}
