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
    ///     The two role names below are translatable in the cloud, but they are <b>not</b> keyed on this
    ///     contract: each block that binds one of these interfaces republishes them under its own
    ///     <see cref="LogicBlockInterfaceBindingAttribute" /> endpoint identifier, so the same role name is
    ///     translated once per binding block, and renaming an endpoint identifier orphans that block's
    ///     copy. See <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockContractAttribute : Attribute
    {
        public required string BetweenInterface { get; init; }

        public required string AndInterface { get; init; }

        /// <summary>
        ///     Human-readable name for the <see cref="BetweenInterface" /> role. Translatable per binding
        ///     block (see the remarks on this attribute); the value here is the source default and the
        ///     permanent fallback.
        /// </summary>
        public string? BetweenDefaultName { get; init; }

        /// <summary>
        ///     Human-readable name for the <see cref="AndInterface" /> role. Translatable per binding block
        ///     (see the remarks on this attribute); the value here is the source default and the permanent
        ///     fallback.
        /// </summary>
        public string? AndDefaultName { get; init; }

        public ContractDirection Direction { get; init; } = ContractDirection.None;
    }
}