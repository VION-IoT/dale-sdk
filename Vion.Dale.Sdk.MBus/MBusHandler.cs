using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Carries every M-Bus binding's reads between the logic blocks and the M-Bus service providers via MQTT. One
    ///     instance serves a runtime.
    /// </summary>
    [InternalApi]
    public partial class MBusHandler : ServiceProviderHandlerBase
    {
        /// <summary>
        ///     The maximum number of reads pending at once, across every M-Bus binding of the runtime.
        /// </summary>
        public const int MaxPendingRequests = 1000;

        private readonly Dictionary<LogicBlockContractId, IActorReference> _actorReferences = [];

        private readonly TimeSpan _checkExpiredRequestsDelay = TimeSpan.FromSeconds(1);

        private readonly Dictionary<ServiceProviderContractId, string> _getResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _getTopics = [];

        private readonly ILogger<MBusHandler> _logger;

        private readonly Dictionary<Guid, PendingRead> _pendingReads = [];

        private readonly TimeProvider _timeProvider;

        private bool _checkExpiredRequestsStarted;

        private bool _requestLimitWasReached;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MBusHandler" /> class.
        /// </summary>
        /// <param name="timeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger used for logging.</param>
        public MBusHandler(TimeProvider timeProvider, ILogger<MBusHandler> logger) : base(logger)
        {
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (MBusTopics.MBus, [$"{MBusTopics.Get}/{ServiceProviderConstants.DaleIdentifier}/response"]);
        }

        /// <inheritdoc />
        protected override void OnContractActorsLinked(LinkLogicBlockContractActors m)
        {
            LogReceivedLinkLogicBlockContractActors(m.ContractLogicBlockActorReferences.Count);

            var linked = new Dictionary<LogicBlockContractId, ServiceProviderContractId>();
            foreach (var (serviceProviderContractId, actorReferences) in m.ContractLogicBlockActorReferences)
            {
                foreach (var (logicBlockContractId, actorReference) in actorReferences)
                {
                    // Kept across maps, unlike the providers: they deliver the receipt of a read a later map leaves unmapped.
                    _actorReferences[logicBlockContractId] = actorReference;
                    if (!linked.TryAdd(logicBlockContractId, serviceProviderContractId))
                    {
                        LogContractAlreadyLinkedToServiceProvider(logicBlockContractId, serviceProviderContractId, linked[logicBlockContractId]);
                    }
                }
            }

            StartCheckExpiredRequestsLoop();
        }

        /// <inheritdoc />
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            if (message.CorrelationId == Guid.Empty)
            {
                LogInvalidCorrelationId(message.Topic);
                return;
            }

            if (message.Topic.Contains(MBusTopics.Get))
            {
                HandleGetResponse(message);
            }
            else
            {
                LogUnhandledTopic(message.CorrelationId, message.Topic);
            }
        }

        /// <inheritdoc />
        protected override void HandleContractMessage(IContractMessage message)
        {
            if (message is ContractMessage<ReadMBusRequest> m)
            {
                PublishGetRequest(m);
            }
        }

        private void HandleGetResponse(ServiceProviderMqttMessage message)
        {
            var correlationId = message.CorrelationId;
            if (!_pendingReads.Remove(correlationId, out var pending))
            {
                LogNoPendingReadFound(correlationId, message.Topic);
                return;
            }

            string responseCode;
            string? errorMessage;
            byte[][]? telegrams = null;
            try
            {
                /*
                 * The response code is read on its own, before the rest of the payload: a code this SDK does not
                 * know comes from a newer contract version and is a mismatch, not a payload that failed to read.
                 */
                using var document = JsonDocument.Parse(message.GetPayloadBytes());
                var root = document.RootElement;
                if (!root.TryGetProperty("responseCode", out var code) || code.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("The answer carries no responseCode.");
                }

                responseCode = code.GetString()!;
                errorMessage = root.TryGetProperty("errorMessage", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                if (responseCode == "Ok")
                {
                    telegrams = root.Deserialize(MBusJsonContext.Default.GetMBusResponsePayload)!.Telegrams ?? [];
                }
            }
            catch (Exception exception)
            {
                // The provider answered but the answer could not be read: a protocol fault, not a refusal.
                Complete(pending,
                         null,
                         new MBusException(MBusOutcome.ProtocolError, $"The M-Bus service provider's answer could not be read: {exception.Message}"),
                         MBusOutcome.ProtocolError,
                         correlationId);
                return;
            }

            var outcome = responseCode switch
            {
                "Ok" => MBusOutcome.Success,
                "NoResponse" => MBusOutcome.NoResponse,
                "Garbled" => MBusOutcome.Garbled,
                "LineUnavailable" => MBusOutcome.LineUnavailable,
                _ => MBusOutcome.Invalid,
            };

            if (outcome == MBusOutcome.Success)
            {
                Complete(pending, telegrams, null, outcome, correlationId);
                return;
            }

            var reason = responseCode switch
            {
                "NotSupported" => "The M-Bus service provider does not support this read.",
                "NoResponse" or "Garbled" or "LineUnavailable" => $"The M-Bus service provider answered {responseCode}.",
                _ => $"The M-Bus service provider answered {responseCode}, a response code this SDK does not know; the provider is of a newer contract version.",
            };
            var detail = string.IsNullOrEmpty(errorMessage) ? reason : $"{reason} {errorMessage}";
            Complete(pending, null, new MBusException(outcome, detail, responseCode), outcome, correlationId);
        }

        private void PublishGetRequest(ContractMessage<ReadMBusRequest> message)
        {
            var logicBlockContractId = message.LogicBlockContractId;
            var request = message.Data;
            LogReceivedReadRequest(logicBlockContractId, request.CorrelationId);

            var unpublished = new PendingRead(logicBlockContractId, request.Callback, request.CreatedAt, null, DateTime.MinValue);

            if (CheckPendingRequestLimit())
            {
                var dropped = new MBusException(MBusOutcome.Dropped,
                                                string.Format(CultureInfo.InvariantCulture,
                                                              "The runtime-wide M-Bus limit of {0} pending reads was reached, so the read was dropped before it was " +
                                                              "published; one handler serves every M-Bus binding on this runtime, so another logic block may be the cause.",
                                                              MaxPendingRequests));
                Complete(unpublished, null, dropped, MBusOutcome.Dropped, request.CorrelationId);
                return;
            }

            var mapped = FindMappedServiceProviderContracts(logicBlockContractId);
            if (mapped.Count == 0)
            {
                var unmapped = new MBusException(MBusOutcome.Invalid, $"No service provider contract mapping found for '{logicBlockContractId}'.");
                Complete(unpublished, null, unmapped, MBusOutcome.Invalid, request.CorrelationId);
                return;
            }

            var serviceProviderContractId = mapped[0];
            var publishedAt = _timeProvider.GetUtcNow().UtcDateTime;
            if (request.MaxQueuedAge is { } maxQueuedAge && publishedAt - request.CreatedAt > maxQueuedAge)
            {
                // Publishing now would put a read nobody waits for any more into the provider's line queue.
                var expired = new MBusException(MBusOutcome.Expired,
                                                string.Format(CultureInfo.InvariantCulture,
                                                              "The read waited {0:0.###} s to reach the M-Bus handler, longer than its maximum queued age of {1:0.###} s, " +
                                                              "so it was not published.",
                                                              (publishedAt - request.CreatedAt).TotalSeconds,
                                                              maxQueuedAge.TotalSeconds));
                Complete(unpublished, null, expired, MBusOutcome.Expired, request.CorrelationId);
                return;
            }

            var requestTopic = GetOrAddGetTopic(serviceProviderContractId);
            var responseTopic = GetOrAddGetResponseTopic(serviceProviderContractId);

            // The operation timeout starts here, so it covers the provider's line queue and not the hop to this handler.
            // Computed before the publish, so the published deadline and this handler's own expiry are one instant.
            var expiresAt = publishedAt + request.OperationTimeout;
            LogPublishingRequest(request.CorrelationId, requestTopic, responseTopic);
            PublishJson(requestTopic,
                        new GetMBusPayload(request.PrimaryAddress, request.MaxTelegrams),
                        MBusJsonContext.Default.GetMBusPayload,
                        nameof(GetMBusPayload),
                        new DateTimeOffset(expiresAt),
                        request.CorrelationId,
                        responseTopic);

            _pendingReads[request.CorrelationId] = new PendingRead(logicBlockContractId, request.Callback, request.CreatedAt, publishedAt, expiresAt);
            LogAddedPendingRead(logicBlockContractId, expiresAt, _pendingReads.Count, request.CorrelationId);
        }

        private void Complete(PendingRead pending, byte[][]? telegrams, MBusException? exception, MBusOutcome outcome, Guid correlationId)
        {
            var logicBlockContractId = pending.LogicBlockContractId;
            if (!_actorReferences.TryGetValue(logicBlockContractId, out var actorReference))
            {
                LogResponseDropped(logicBlockContractId, correlationId, outcome);
                return;
            }

            // Stamped here, the moment the answer was observed, so a block slow to drain its mailbox cannot move it.
            var response = new ReadMBusResponse(telegrams,
                                                exception,
                                                pending.Callback,
                                                correlationId,
                                                pending.CreatedAt,
                                                pending.PublishedAt,
                                                _timeProvider.GetUtcNow().UtcDateTime,
                                                _timeProvider.GetTimestamp(),
                                                outcome);
            ActorContext.SendTo(actorReference, new ContractMessage<ReadMBusResponse>(logicBlockContractId, response));
        }

        private string GetOrAddGetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_getTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = CreateGetTopic(serviceProviderContractId);
                _getTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private string GetOrAddGetResponseTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_getResponseTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = $"{CreateGetTopic(serviceProviderContractId)}/{ServiceProviderConstants.DaleIdentifier}/response";
                _getResponseTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private static string CreateGetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            return
                $"{MqttConfiguration.InstallationTopic}/{serviceProviderContractId.ServiceProviderIdentifier}/{serviceProviderContractId.ServiceIdentifier}/{serviceProviderContractId.ContractIdentifier}{MBusTopics.Get}";
        }

        private bool CheckPendingRequestLimit()
        {
            var limitReached = _pendingReads.Count >= MaxPendingRequests;
            switch (limitReached)
            {
                case true when !_requestLimitWasReached: LogPendingRequestsLimitReached(MaxPendingRequests); break;
                case false when _requestLimitWasReached: LogPendingRequestsDroppedBelowLimit(_pendingReads.Count, MaxPendingRequests); break;
            }

            _requestLimitWasReached = limitReached;

            return limitReached;
        }

        private void StartCheckExpiredRequestsLoop()
        {
            if (_checkExpiredRequestsStarted)
            {
                return;
            }

            _checkExpiredRequestsStarted = true;
            ScheduleCheckExpiredRequests();
        }

        private void CompleteExpiredRequests()
        {
            ScheduleCheckExpiredRequests();
            if (_pendingReads.Count == 0)
            {
                return;
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var (correlationId, pending) in _pendingReads)
            {
                if (utcNow < pending.ExpiresAt)
                {
                    continue;
                }

                LogCompletingExpiredRead(pending.CreatedAt, pending.ExpiresAt, correlationId, pending.LogicBlockContractId);
                _pendingReads.Remove(correlationId);
                var timeout = new MBusException(MBusOutcome.Timeout,
                                                string.Format(CultureInfo.InvariantCulture,
                                                              "The M-Bus service provider did not answer within the operation timeout of {0:0.###} s.",
                                                              (pending.ExpiresAt - pending.PublishedAt!.Value).TotalSeconds));
                Complete(pending, null, timeout, MBusOutcome.Timeout, correlationId);
            }
        }

        private void ScheduleCheckExpiredRequests()
        {
            InvokeSynchronizedAfter(CompleteExpiredRequests, _checkExpiredRequestsDelay);
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received link logic block contract actors message (ServiceProviderContracts={ServiceProviderContracts})")]
        private partial void LogReceivedLinkLogicBlockContractActors(int serviceProviderContracts);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Contract cannot be linked to service provider because this link map already links it to another. " +
                                 "(LogicBlockContractId={LogicBlockContractId}, AttemptedServiceProviderContractId={AttemptedServiceProviderContractId}, LinkedServiceProviderContractId={LinkedServiceProviderContractId})")]
        private partial void LogContractAlreadyLinkedToServiceProvider(LogicBlockContractId logicBlockContractId,
                                                                       ServiceProviderContractId attemptedServiceProviderContractId,
                                                                       ServiceProviderContractId linkedServiceProviderContractId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Received MQTT message with missing or invalid correlation ID (Topic={Topic})")]
        private partial void LogInvalidCorrelationId(string topic);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled MQTT topic (CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogUnhandledTopic(Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No pending read found for correlation ID — read probably expired (CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogNoPendingReadFound(Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received read request (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogReceivedReadRequest(LogicBlockContractId logicBlockContractId, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing read (RequestTopic={RequestTopic}, ResponseTopic={ResponseTopic}, CorrelationId={CorrelationId})")]
        private partial void LogPublishingRequest(Guid correlationId, string requestTopic, string responseTopic);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Added pending read (LogicBlockContractId={LogicBlockContractId}, ExpiresAt={ExpiresAt}, PendingReads={PendingReads}, CorrelationId={CorrelationId})")]
        private partial void LogAddedPendingRead(LogicBlockContractId logicBlockContractId, DateTime expiresAt, int pendingReads, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Response dropped — no actor reference found for contract — cannot send it to the logic block " +
                                 "(LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId}, Outcome={Outcome})")]
        private partial void LogResponseDropped(LogicBlockContractId logicBlockContractId, Guid correlationId, MBusOutcome outcome);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Pending reads limit reached — cannot add new reads (PendingRequestLimit={PendingRequestLimit})")]
        private partial void LogPendingRequestsLimitReached(int pendingRequestLimit);

        [LoggerMessage(Level = LogLevel.Information, Message = "Pending reads dropped below limit (PendingReads={PendingReads}, PendingRequestLimit={PendingRequestLimit})")]
        private partial void LogPendingRequestsDroppedBelowLimit(int pendingReads, int pendingRequestLimit);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Completing expired read, created at {CreatedAt}, should have expired at {ExpiresAt} (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogCompletingExpiredRead(DateTime createdAt, DateTime expiresAt, Guid correlationId, LogicBlockContractId logicBlockContractId);

        private readonly record struct PendingRead(
            LogicBlockContractId LogicBlockContractId,
            Action<byte[][]?, MBusException?, MBusReceipt> Callback,
            DateTime CreatedAt,
            DateTime? PublishedAt,
            DateTime ExpiresAt);
    }
}