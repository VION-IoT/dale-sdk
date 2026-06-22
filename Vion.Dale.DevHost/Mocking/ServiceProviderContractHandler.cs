using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    /// <summary>
    ///     The one generic DevHost stand-in for a service-provider <b>value</b> contract (RFC 0010): it replaces
    ///     the four hardcoded <c>MockHal*Handler</c> classes and is created once per discovered
    ///     <see cref="IServiceProviderHandlerActor" /> type under that handler's class name (the name the
    ///     consumer's contract already looks up), so no production code path changes. Its
    ///     <see cref="ScenarioWireCodec" /> — built from the handler's <c>[ScenarioWire]</c> — knows the exact
    ///     wire struct, so a digital input and a third-party multi-field contract (PPC) drive through the same
    ///     code with no per-contract handler (the DF-27 unblock).
    ///     <para>
    ///         It is a plain <see cref="IActorReceiver" />, not a <c>ServiceProviderHandlerBase</c>: the base's
    ///         sealed dispatch routes only MQTT/contract messages and cannot receive the DevHost-only drive /
    ///         publish messages, and nothing casts a looked-up handler to <see cref="IServiceProviderHandlerActor" />.
    ///     </para>
    ///     <para>
    ///         <b>Transitional HAL bridge.</b> For the four built-in HAL contracts (single scalar values) it
    ///         additionally raises the typed <see cref="DevHostEvents" /> (the Tier 1 output cache + the SPA I/O
    ///         panel both read these) and echoes the output-confirmation back to the block (output-confirmation
    ///         consumers subscribe to it) — discriminating digital from analog by JSON value kind. That keeps the
    ///         four <c>digitalInput</c>/<c>analogInput</c>/<c>digitalOutput</c>/<c>analogOutput</c> step kinds
    ///         behaviourally identical until the generic <c>serviceProviderSet</c>/<c>serviceProviderExpect</c>
    ///         path (RFC 0010 increment 3) and format v2 (increment 5) replace them. A custom value contract
    ///         (object/array payload) is forwarded fire-and-forget with no typed event or echo, which is the
    ///         value-contract model.
    ///     </para>
    /// </summary>
    internal sealed class ServiceProviderContractHandler : IActorReceiver
    {
        private readonly ScenarioWireCodec _codec;

        private readonly DevHostEvents _events;

        private readonly Control.ServiceProviderOutputCache _outputCache;

        // Last driven / written value per contract, replayed on MockPublishAllStatesMessage so a late web
        // subscriber sees current HAL state (the four mock handlers' PublishAllStates behaviour).
        private readonly Dictionary<ServiceProviderContractId, JsonElement> _lastInbound = new();

        private readonly Dictionary<ServiceProviderContractId, JsonElement> _lastOutbound = new();

        private readonly ILogger _logger;

        private Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> _contractLogicBlockActorReferences = new();

        public ServiceProviderContractHandler(ILogger logger, DevHostEvents events, ScenarioWireCodec codec, Control.ServiceProviderOutputCache outputCache)
        {
            _logger = logger;
            _events = events;
            _codec = codec;
            _outputCache = outputCache;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case LinkLogicBlockContractActors m: // Initialization: the full link map (all contracts), unfiltered.
                    _contractLogicBlockActorReferences = m.ContractLogicBlockActorReferences;
                    break;

                case MockSetServiceProviderInputMessage m: // Drive an input contract (scenario / control surface).
                    Drive(m.Contract, m.Value, actorContext);
                    break;

                case IContractMessage m: // An output command a block Set on this contract.
                    Capture(m, actorContext);
                    break;

                case MockPublishAllStatesMessage: // Replay current HAL state for a late web subscriber.
                    PublishAllStates();
                    break;
            }

            return Task.CompletedTask;
        }

        // Build the exact closed ContractMessage<TInbound> from the scenario value and forward it to every
        // logic block mapped to the contract — the same CLR payload the production handler forwards from a
        // FlatBuffer frame, sourced from JSON instead.
        private void Drive(ServiceProviderContractId contract, JsonElement value, IActorContext actorContext)
        {
            if (!_codec.CanDrive)
            {
                _logger.LogWarning("Drive ignored for {Contract}: its [ScenarioWire] declares no inbound struct (it is an output contract).", contract);
                return;
            }

            _lastInbound[contract] = value;
            RaiseInputChanged(contract, value);

            if (!_contractLogicBlockActorReferences.TryGetValue(contract, out var blocks))
            {
                _logger.LogDebug("Drive for {Contract} has no mapped logic blocks; nothing forwarded.", contract);
                return;
            }

            foreach (var (logicBlockContractId, logicBlockActorRef) in blocks)
            {
                actorContext.SendTo(logicBlockActorRef, _codec.MakeInbound(logicBlockContractId, value));
            }
        }

        // Decode the command a block wrote (so serviceProviderExpect / GetDigitalOutput can read it back),
        // and — for HAL scalar contracts — echo the confirmation to the block exactly as the mock did.
        private void Capture(IContractMessage message, IActorContext actorContext)
        {
            if (!_codec.CanAssert)
            {
                _logger.LogDebug("Ignoring a contract message on an input-only handler ({Contract}).", message.LogicBlockContractId);
                return;
            }

            foreach (var (contract, blocks) in _contractLogicBlockActorReferences)
            {
                if (!blocks.ContainsKey(message.LogicBlockContractId))
                {
                    continue;
                }

                var value = _codec.ReadCommand(message);
                _lastOutbound[contract] = value;
                _outputCache.Record(contract, value); // The generic read source for serviceProviderExpect (any value contract).
                RaiseOutputChanged(contract, value);
                EchoOutputConfirmation(blocks, value, actorContext);
                return;
            }
        }

        private void PublishAllStates()
        {
            foreach (var (contract, value) in _lastInbound)
            {
                RaiseInputChanged(contract, value);
            }

            foreach (var (contract, value) in _lastOutbound)
            {
                RaiseOutputChanged(contract, value);
            }
        }

        // ── Transitional HAL bridge ──────────────────────────────────────────────────────────────────────
        // The typed DevHostEvents + output echo for the four built-in scalar HAL contracts. Removed when the
        // generic serviceProviderSet/serviceProviderExpect path supersedes the four step kinds (RFC 0010).

        private void RaiseInputChanged(ServiceProviderContractId contract, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True or JsonValueKind.False:
                    _events.RaiseDigitalInputChanged(contract.ServiceProviderIdentifier, contract.ServiceIdentifier, contract.ContractIdentifier, value.GetBoolean());
                    break;
                case JsonValueKind.Number:
                    _events.RaiseAnalogInputChanged(contract.ServiceProviderIdentifier, contract.ServiceIdentifier, contract.ContractIdentifier, value.GetDouble());
                    break;
            }
        }

        private void RaiseOutputChanged(ServiceProviderContractId contract, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True or JsonValueKind.False:
                    _events.RaiseDigitalOutputChanged(contract.ServiceProviderIdentifier, contract.ServiceIdentifier, contract.ContractIdentifier, value.GetBoolean());
                    break;
                case JsonValueKind.Number:
                    _events.RaiseAnalogOutputChanged(contract.ServiceProviderIdentifier, contract.ServiceIdentifier, contract.ContractIdentifier, value.GetDouble());
                    break;
            }
        }

        private static void EchoOutputConfirmation(Dictionary<LogicBlockContractId, IActorReference> blocks, JsonElement value, IActorContext actorContext)
        {
            foreach (var (logicBlockContractId, logicBlockActorRef) in blocks)
            {
                object? confirmation = value.ValueKind switch
                                       {
                                           JsonValueKind.True or JsonValueKind.False => new ContractMessage<DigitalOutputChanged>(logicBlockContractId,
                                                                                            new DigitalOutputChanged(value.GetBoolean())),
                                           JsonValueKind.Number => new ContractMessage<AnalogOutputChanged>(logicBlockContractId, new AnalogOutputChanged(value.GetDouble())),
                                           _ => null,
                                       };

                if (confirmation is not null)
                {
                    actorContext.SendTo(logicBlockActorRef, confirmation);
                }
            }
        }
    }
}
