using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    /// <summary>
    ///     Base class for all logic block contract implementations (e.g., DigitalInput, DigitalOutput, ModbusRtu).
    ///     A contract represents a binding between a logic block and a service provider endpoint.
    ///     It receives state updates from the service provider handler and can send commands back to it.
    /// </summary>
    /// <remarks>
    ///     Subclasses must:
    ///     <list type="bullet">
    ///         <item>
    ///             Set <see cref="ContractHandlerActorName" /> to the name of the handler actor (e.g.,
    ///             <c>nameof(DigitalInputHandler)</c>)
    ///         </item>
    ///         <item>Implement <see cref="HandleContractMessage" /> to dispatch incoming messages (state changes, responses)</item>
    ///     </list>
    ///     Subclasses that send commands to the handler (output contracts, request-response contracts) use
    ///     <see cref="SendToContractHandler{T}" /> to send messages to the linked handler actor.
    /// </remarks>
    [PublicApi]
    public abstract class LogicBlockContractBase
    {
        private readonly IActorContext _actorContext;

        private IActorReference _contractHandlerActorRef = null!;

        /// <summary>
        ///     The identity of this contract within its owning logic block. Set during initialization.
        /// </summary>
        protected LogicBlockContractId LogicBlockContractId { get; private set; }

        /// <summary>
        ///     The name of the handler actor this contract communicates with.
        ///     Must match the actor name registered in the runtime (e.g., <c>nameof(DigitalInputHandler)</c>).
        /// </summary>
        public abstract string ContractHandlerActorName { get; protected set; }

        /// <summary>
        ///     The contract identifier as declared on the logic block property (e.g., <c>"di0"</c>).
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        ///     Metadata for this contract (default name, tags, cardinality, sharing).
        ///     Populated from attributes during introspection.
        /// </summary>
        public ContractMetaData MetaData { get; } = new();

        /// <summary>
        ///     Initializes a new instance of the contract.
        /// </summary>
        /// <param name="identifier">The contract identifier (matches the property name on the logic block).</param>
        /// <param name="actorContext">The actor context used to send messages to the handler actor.</param>
        protected LogicBlockContractBase(string identifier, IActorContext actorContext)
        {
            _actorContext = actorContext;
            Identifier = identifier;
            LogicBlockContractId = LogicBlockContractId with { ContractIdentifier = identifier };
        }

        /// <summary>
        ///     Sets the full logic block contract identity. Called by the runtime during initialization
        ///     to associate this contract with its owning logic block.
        /// </summary>
        /// <param name="logicBlockContractId">The full contract identity including the logic block ID.</param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when <paramref name="logicBlockContractId" /> has a contract identifier that does not match
        ///     <see cref="Identifier" />.
        /// </exception>
        public void SetLogicBlockContractId(LogicBlockContractId logicBlockContractId)
        {
            if (logicBlockContractId.ContractIdentifier != Identifier)
            {
                throw new InvalidOperationException($"LogicBlockContractId identifier {logicBlockContractId.ContractIdentifier} does not match contract identifier {Identifier}");
            }

            LogicBlockContractId = LogicBlockContractId with { LogicBlockId = logicBlockContractId.LogicBlockId };
        }

        /// <summary>
        ///     Links this contract to its handler actor. Called by the runtime during initialization.
        /// </summary>
        /// <param name="contractHandlerActorRef">A reference to the handler actor.</param>
        public void SetLinkedContractHandler(IActorReference contractHandlerActorRef)
        {
            _contractHandlerActorRef = contractHandlerActorRef;
        }

        /// <summary>
        ///     Dispatches an incoming contract message (e.g., state change, response) to the appropriate handler logic.
        ///     Called by the runtime when a message from the handler actor targets this contract.
        /// </summary>
        /// <param name="contractMessage">The incoming contract message.</param>
        public abstract void HandleContractMessage(IContractMessage contractMessage);

        /// <summary>
        ///     Sends a message to the linked handler actor (e.g., a set command or a read request).
        ///     If the contract has no mapping (no linked logic block), the message is silently dropped.
        /// </summary>
        /// <typeparam name="T">The message type (must be a struct).</typeparam>
        /// <param name="message">The message to send.</param>
        protected void SendToContractHandler<T>(T message)
            where T : struct
        {
            if (string.IsNullOrEmpty(LogicBlockContractId.LogicBlockId.Id))
            {
                // Contract has no mapping — silently drop the message.
                // A warning is already logged at startup by LogicBlockBase.
                return;
            }

            _actorContext.SendTo(_contractHandlerActorRef, message);
        }
    }
}