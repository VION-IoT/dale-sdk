using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a class as a contract container grouping related messages and interfaces.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class ContractAttribute : Attribute
    {
        public required string BetweenInterface { get; init; }

        public required string AndInterface { get; init; }

        public string? BetweenDefaultName { get; init; }

        public string? AndDefaultName { get; init; }

        public ContractDirection Direction { get; init; } = ContractDirection.None;
    }
}