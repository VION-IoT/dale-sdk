using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a class as a contract container grouping messages
    ///     (<see cref="CommandAttribute" />, <see cref="StateUpdateAttribute" />,
    ///     <see cref="RequestResponseAttribute" />) exchanged between two LogicBlock
    ///     interfaces.
    /// </summary>
    /// <remarks>
    ///     The role names below are translatable in the cloud, but not keyed on this contract: each block
    ///     that binds one of these interfaces carries its own copy, keyed by that block's
    ///     <see cref="LogicBlockInterfaceBindingAttribute" /> identifier. See
    ///     <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockContractAttribute : Attribute
    {
        public required string BetweenInterface { get; init; }

        public required string AndInterface { get; init; }

        /// <summary>
        ///     Human-readable name for the <see cref="BetweenInterface" /> role. Translatable per binding
        ///     block (see the remarks on this attribute).
        /// </summary>
        public string? BetweenDefaultName { get; init; }

        /// <summary>
        ///     Human-readable name for the <see cref="AndInterface" /> role. Translatable per binding block
        ///     (see the remarks on this attribute).
        /// </summary>
        public string? AndDefaultName { get; init; }

        public ContractDirection Direction { get; init; } = ContractDirection.None;
    }
}