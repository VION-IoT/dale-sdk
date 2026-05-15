using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Base class for all service provider handler actors (DI, DO, AI, AO, Modbus, custom).
    ///     Owns the actor lifecycle (registration, contract linking) and provides helpers for
    ///     common operations.
    /// </summary>
    /// <remarks>
    ///     <see cref="IActorReceiver.HandleMessageAsync" /> is implemented explicitly so subclasses
    ///     cannot override it. Messages are routed to:
    ///     <list type="bullet">
    ///         <item><see cref="HandleMqttMessage" /> — for MQTT messages from the broker</item>
    ///         <item><see cref="HandleContractMessage" /> — for contract messages from logic blocks</item>
    ///     </list>
    ///     Subclasses can schedule delayed callbacks using <see cref="InvokeSynchronizedAfter" />,
    ///     which are dispatched transparently by the base class (same pattern as <c>LogicBlockBase</c>).
    /// </remarks>
    [PublicApi]
    public abstract class ServiceProviderHandlerBase : IServiceProviderHandlerActor
    {
        /// <summary>
        ///     The wildcard prefix prepended to all subscription action paths.
        ///     Matches the <c>/{serviceProviderIdentifier}/{service}/{contract}</c> routing prefix
        ///     in the topic structure. Centralized here to enforce the convention and enable
        ///     programmatic broker ACL configuration.
        /// </summary>
        private const string ServiceProviderTopicPrefix = "/+/+/+";

        /// <summary>
        ///     The current actor context. Set on each message dispatch.
        ///     Available for use in <see cref="InvokeSynchronizedAfter" /> callbacks.
        /// </summary>
        protected IActorContext ActorContext { get; private set; } = null!;

        /// <summary>
        ///     Logger available to subclasses.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        ///     The contract-to-logic-block actor mappings, set during the linking phase.
        /// </summary>
        protected Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> ContractLogicBlockActorReferences { get; private set; } = [];

        /// <summary>
        ///     Initializes a new instance of the handler.
        /// </summary>
        protected ServiceProviderHandlerBase(ILogger logger)
        {
            Logger = logger;
        }

        /// <inheritdoc />
        Task IActorReceiver.HandleMessageAsync(object message, IActorContext actorContext)
        {
            ActorContext = actorContext;

            switch (message)
            {
                case RegisterMqttHandlerRequest:
                    var (routingKey, actionPaths) = GetMqttRegistration();
                    var topics = new string[actionPaths.Length];
                    for (var i = 0; i < actionPaths.Length; i++)
                    {
                        topics[i] = $"{ServiceProviderTopicPrefix}{actionPaths[i]}";
                    }

                    this.RegisterWithMqttClient(routingKey, topics, actorContext, Logger);
                    break;
                case LinkLogicBlockContractActors m:
                    ContractLogicBlockActorReferences = m.ContractLogicBlockActorReferences;
                    OnContractActorsLinked(m);
                    break;
                case InvokeActionMessage m:
                    m.Action();
                    break;
                case MqttMessageReceived m:
                    var spMessage = new ServiceProviderMqttMessage(m, m.ExtractServiceProviderContractId(), m.TryGetCorrelationId());
                    HandleMqttMessage(spMessage);
                    break;
                case IContractMessage m:
                    HandleContractMessage(m);
                    break;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Returns the MQTT routing key and action path suffixes for this handler.
        ///     The base class prepends the service provider wildcard prefix (<c>/+/+/+</c>)
        ///     to each action path to form the full subscription topics.
        /// </summary>
        /// <returns>
        ///     A tuple of the routing key (used for message dispatch, e.g., <c>Topics.Di</c>)
        ///     and action paths (the contract-specific suffixes, e.g., <c>Topics.DiState</c>).
        ///     Action paths must start with <c>/</c> (all <c>Topics.*</c> constants follow this convention).
        /// </returns>
        protected abstract (string RoutingKey, string[] ActionPaths) GetMqttRegistration();

        /// <summary>
        ///     Handles an MQTT message received from the broker. The message contains
        ///     the pre-parsed <see cref="ServiceProviderMqttMessage.ContractId" /> and
        ///     <see cref="ServiceProviderMqttMessage.CorrelationId" />.
        ///     Use <see cref="ActorContext" /> for actor communication.
        /// </summary>
        protected abstract void HandleMqttMessage(ServiceProviderMqttMessage message);

        /// <summary>
        ///     Handles a contract message from a logic block (e.g., set commands, read/write requests).
        ///     Use <see cref="ActorContext" /> for actor communication.
        /// </summary>
        protected abstract void HandleContractMessage(IContractMessage message);

        /// <summary>
        ///     Called after contract actor references are linked. Override to perform additional setup
        ///     (e.g., building per-contract lookup dictionaries).
        /// </summary>
        protected virtual void OnContractActorsLinked(LinkLogicBlockContractActors message)
        {
        }

        // ── Scheduling ───────────────────────────────────────────────────────────

        /// <summary>
        ///     Schedules an action to be invoked after a delay, dispatched through the actor's message loop.
        ///     Same pattern as <c>LogicBlockBase.InvokeSynchronizedAfter</c>.
        /// </summary>
        protected void InvokeSynchronizedAfter(Action action, TimeSpan delay)
        {
            ActorContext.SendToSelfAfter(new InvokeActionMessage(action), delay);
        }

        // ── Publishing ───────────────────────────────────────────────────────────

        /// <summary>
        ///     Publishes an MQTT message with the standard protocol conventions (correlation ID, schema user property,
        ///     content type). Returns the correlation ID used.
        /// </summary>
        /// <param name="topic">The full MQTT topic to publish to.</param>
        /// <param name="payload">The serialized payload bytes.</param>
        /// <param name="schemaName">The schema name set as an MQTT user property (identifies the payload type).</param>
        /// <param name="contentType">
        ///     The MQTT content type (e.g., <c>MessageMimeTypes.FlatBuffer</c>, <c>MessageMimeTypes.Json</c>).
        ///     Defaults to <c>MessageMimeTypes.FlatBuffer</c> if not specified.
        /// </param>
        /// <param name="correlationId">An existing correlation ID to use. If <c>null</c>, a new one is generated.</param>
        /// <param name="responseTopic">Optional response topic for request-response patterns.</param>
        /// <param name="retain">Whether the message should be retained by the broker.</param>
        /// <returns>The correlation ID used for the published message.</returns>
        protected Guid Publish(string topic,
                               byte[] payload,
                               string schemaName,
                               string? contentType = null,
                               Guid? correlationId = null,
                               string? responseTopic = null,
                               bool retain = false)
        {
            var id = correlationId ?? Guid.NewGuid();
            var schema = new MqttUserProperty(MqttUserProperties.Schema.Name, schemaName);
            var mqttMessage = new PublishMqttMessage(topic,
                                                     payload,
                                                     contentType,
                                                     id.ToByteArray(),
                                                     responseTopic,
                                                     [schema],
                                                     retain);
            ActorContext.SendTo(ActorContext.LookupByName(MqttConstants.MqttClientName), mqttMessage);
            return id;
        }

        /// <summary>
        ///     Serializes the payload as JSON and publishes it with <c>application/json</c> content type.
        /// </summary>
        /// <inheritdoc cref="Publish" />
        protected Guid PublishJson<T>(string topic,
                                      T payload,
                                      string schemaName,
                                      Guid? correlationId = null,
                                      string? responseTopic = null,
                                      bool retain = false)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerialization.DefaultOptions);
            return Publish(topic,
                           bytes,
                           schemaName,
                           MessageMimeTypes.Json,
                           correlationId,
                           responseTopic,
                           retain);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        ///     Forwards a state-changed message to all logic block actors mapped to the given service provider contract.
        /// </summary>
        protected void ForwardToLogicBlocks<TChanged>(ServiceProviderContractId contractId, TChanged changed)
            where TChanged : struct
        {
            if (!ContractLogicBlockActorReferences.TryGetValue(contractId, out var contractMappings))
            {
                Logger.LogDebug("No logic block contract mappings found (ServiceProviderContractId={ServiceProviderContractId})", contractId);
                return;
            }

            foreach (var (logicBlockContractId, logicBlockActorRef) in contractMappings)
            {
                ActorContext.SendTo(logicBlockActorRef, new ContractMessage<TChanged>(logicBlockContractId, changed));
            }
        }

        /// <summary>
        ///     Finds all service provider contracts that a logic block contract is mapped to.
        ///     Used by output handlers to reverse-lookup the target when a logic block sends a set command.
        /// </summary>
        protected List<ServiceProviderContractId> FindMappedServiceProviderContracts(LogicBlockContractId logicBlockContractId)
        {
            List<ServiceProviderContractId> result = [];
            foreach (var (serviceProviderContractId, actorReferencesByContractId) in ContractLogicBlockActorReferences)
            {
                if (actorReferencesByContractId.ContainsKey(logicBlockContractId))
                {
                    result.Add(serviceProviderContractId);
                }
            }

            return result;
        }

        private readonly record struct InvokeActionMessage(Action Action);
    }
}
