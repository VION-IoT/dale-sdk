using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
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
    ///         <b>Every contract is handled the same way — there is no HAL special case.</b> A command a block
    ///         writes is decoded, recorded in the <see cref="Control.ServiceProviderOutputCache" /> (the read
    ///         source for <c>serviceProviderExpect</c>) and raised as the one generic
    ///         <c>ServiceProviderContractChanged</c> event the SPA renders. It is <b>not echoed back</b> to the
    ///         block: in production the confirmation comes from the provider over MQTT, so an output's
    ///         <c>OutputChanged</c> fires here only when something drives the contract's declared inbound —
    ///         a <c>serviceProviderSet</c>, or the control surface. That is what makes a wrong confirmation
    ///         (commanded ≠ confirmed) testable. There are no typed per-family events or caches.
    ///     </para>
    /// </summary>
    internal sealed class ServiceProviderContractHandler : IActorReceiver
    {
        private readonly ScenarioWireCodec _codec;

        private readonly DevHostEvents _events;

        // Last driven / written value per contract, replayed on MockPublishAllStatesMessage so a late web
        // subscriber sees current HAL state (the four mock handlers' PublishAllStates behaviour).
        private readonly Dictionary<ServiceProviderContractId, JsonElement> _lastInbound = new();

        private readonly Dictionary<ServiceProviderContractId, JsonElement> _lastOutbound = new();

        private readonly ILogger _logger;

        private readonly Control.ServiceProviderOutputCache _outputCache;

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
                    Capture(m);
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
                _logger.LogWarning("Drive ignored for {Contract}: its [ScenarioWire] declares no inbound struct, so there is nothing to deliver to the block.", contract);
                return;
            }

            _lastInbound[contract] = value;
            RaiseContractChanged(contract, value);

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

        // Decode the command a block wrote so serviceProviderExpect can read it back, and raise the generic
        // value-changed event for the live UI. No HAL-specific echo: the real upstream confirms over MQTT; the
        // DevHost does not synthesize a typed output-confirmation.
        private void Capture(IContractMessage message)
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
                _outputCache.Record(contract, value); // The read source for serviceProviderExpect (any value contract).
                RaiseContractChanged(contract, value);
                return;
            }
        }

        private void PublishAllStates()
        {
            foreach (var (contract, value) in _lastInbound)
            {
                RaiseContractChanged(contract, value);
            }

            foreach (var (contract, value) in _lastOutbound)
            {
                RaiseContractChanged(contract, value);
            }
        }

        // Raise the one generic value-changed event (RFC 0010) for any value contract — the SPA wiring panel
        // renders the JSON value per the contract's own type. No digital/analog discrimination here.
        private void RaiseContractChanged(ServiceProviderContractId contract, JsonElement value)
        {
            _events.RaiseServiceProviderContractChanged(contract.ServiceProviderIdentifier, contract.ServiceIdentifier, contract.ContractIdentifier, value);
        }
    }
}