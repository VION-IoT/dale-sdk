using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Contracts.Mqtt;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Handles communication between logic block Modbus RTU and the HAL via MQTT.
    /// </summary>
    public partial class ModbusRtuHandler : ServiceProviderHandlerBase
    {
        /// <summary>
        ///     The maximum number of pending requests (reads and writes combined).
        /// </summary>
        public const int MaxPendingRequests = 1000;

        private readonly Dictionary<LogicBlockContractId, IActorReference> _actorReferences = [];

        private readonly TimeSpan _checkExpiredRequestsDelay = TimeSpan.FromSeconds(1);

        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly Dictionary<ServiceProviderContractId, string> _getResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _getTopics = [];

        private readonly ILogger<ModbusRtuHandler> _logger;

        private readonly Dictionary<Guid, PendingReadRequest> _pendingReadRequests = [];

        private readonly Dictionary<Guid, PendingWriteRequest> _pendingWriteRequests = [];

        private readonly PendingRequestsLimitReachedException _requestLimitException = new(MaxPendingRequests);

        private readonly Dictionary<LogicBlockContractId, ServiceProviderContractId> _serviceProviderContractIds = [];

        private readonly Dictionary<ServiceProviderContractId, string> _setResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _setTopics = [];

        private readonly OperationTimeoutException _timeoutException = new();

        private bool _checkExpiredRequestsStarted;

        private bool _requestLimitWasReached;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusRtuHandler" /> class.
        /// </summary>
        /// <param name="dateTimeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger used for logging.</param>
        public ModbusRtuHandler(IDateTimeProvider dateTimeProvider, ILogger<ModbusRtuHandler> logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (Topics.Modbus, [
                           $"{Topics.ModbusGet}/{ServiceProviderConstants.DaleIdentifier}/response",
                           $"{Topics.ModbusSet}/{ServiceProviderConstants.DaleIdentifier}/response",
                       ]);
        }

        /// <inheritdoc />
        protected override void OnContractActorsLinked(LinkLogicBlockContractActors m)
        {
            LogReceivedLinkLogicBlockContractActors(m.ContractLogicBlockActorReferences.Count);
            foreach (var (serviceProviderContractId, actorReferences) in m.ContractLogicBlockActorReferences)
            {
                foreach (var (logicBlockContractId, actorReference) in actorReferences)
                {
                    _actorReferences[logicBlockContractId] = actorReference;
                    if (_serviceProviderContractIds.TryGetValue(logicBlockContractId, out var linkedServiceProviderContractId))
                    {
                        LogContractAlreadyLinkedToServiceProvider(logicBlockContractId, serviceProviderContractId, linkedServiceProviderContractId);
                    }
                    else
                    {
                        _serviceProviderContractIds[logicBlockContractId] = serviceProviderContractId;
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

            _logger.LogHandlingMqttMessage(message.CorrelationId, message.Topic);
            switch (message.Topic)
            {
                case var topic when topic.Contains(Topics.ModbusGet):
                    HandleGetResponse(message);
                    break;
                case var topic when topic.Contains(Topics.ModbusSet):
                    HandleSetResponse(message);
                    break;
                default:
                    _logger.LogUnhandledMqttTopic(message.CorrelationId, message.Topic);
                    break;
            }
        }

        /// <inheritdoc />
        protected override void HandleContractMessage(IContractMessage message)
        {
            switch (message)
            {
                case ContractMessage<ReadModbusRtuRequest> m:
                    PublishModbusGetRequest(m);
                    break;
                case ContractMessage<WriteModbusRtuRequest> m:
                    PublishModbusSetRequest(m);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received link logic block contract actors message (ServiceProviderContracts={ServiceProviderContracts})")]
        private partial void LogReceivedLinkLogicBlockContractActors(int serviceProviderContracts);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Contract cannot be linked to service provider because it is already linked. " +
                                 "(LogicBlockContractId={LogicBlockContractId}, AttemptedServiceProviderContractId={AttemptedServiceProviderContractId}, LinkedServiceProviderContractId={LinkedServiceProviderContractId})")]
        private partial void LogContractAlreadyLinkedToServiceProvider(LogicBlockContractId logicBlockContractId,
                                                                       ServiceProviderContractId attemptedServiceProviderContractId,
                                                                       ServiceProviderContractId linkedServiceProviderContractId);

        #region ModbusMessageHandling

        #region Read

        private void HandleGetResponse(ServiceProviderMqttMessage message)
        {
            var correlationId = message.CorrelationId;
            if (!_pendingReadRequests.Remove(correlationId, out var pendingRequest))
            {
                LogNoPendingReadRequestFound(correlationId, message.Topic);
                return;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var elapsedMs = _dateTimeProvider.GetElapsedTime(pendingRequest.CreatedAt).TotalMilliseconds;
                LogReceivedReadResponse(pendingRequest.LogicBlockContractId, pendingRequest.CreatedAt, elapsedMs, correlationId);
            }

            try
            {
                var payload = GetModbusResponsePayload.GetRootAsGetModbusResponsePayload(message.GetFlatBufferPayload());
                if (payload.ResponseCode == ModbusResponseCode.Ok)
                {
                    PublishResponse(pendingRequest.LogicBlockContractId, payload.GetDataArray(), null, pendingRequest.Callback, correlationId);
                }
                else
                {
                    var modbusException = new ModbusException((ModbusExceptionCode)payload.ResponseCode, payload.ErrorMessage ?? string.Empty);
                    PublishResponse(pendingRequest.LogicBlockContractId, null, modbusException, pendingRequest.Callback, correlationId);
                }
            }
            catch (Exception exception)
            {
                PublishResponse(pendingRequest.LogicBlockContractId, null, exception, pendingRequest.Callback, correlationId);
            }
        }

        private void PublishModbusGetRequest(ContractMessage<ReadModbusRtuRequest> message)
        {
            var logicBlockContractId = message.LogicBlockContractId;
            var request = message.Data;
            LogReceivedReadRequest(logicBlockContractId, request.CorrelationId);

            var limitReached = CheckPendingRequestLimit();
            if (limitReached)
            {
                PublishResponse(logicBlockContractId, null, _requestLimitException, request.Callback, request.CorrelationId);
                return;
            }

            if (!_serviceProviderContractIds.TryGetValue(logicBlockContractId, out var serviceProviderContractId))
            {
                PublishResponse(logicBlockContractId, null, new ServiceProviderContractMappingNotFoundException(logicBlockContractId), request.Callback, request.CorrelationId);
                return;
            }

            if (_dateTimeProvider.UtcNow >= request.ExpiresAt)
            {
                PublishResponse(logicBlockContractId, null, _timeoutException, request.Callback, request.CorrelationId);
                return;
            }

            var requestTopic = GetOrAddGetTopic(serviceProviderContractId);
            var responseTopic = GetOrAddGetResponseTopic(serviceProviderContractId);
            var payload = CreateGetModbusPayload(request.FunctionCode, request.UnitId, request.StartingAddress, request.Quantity);
            PublishRequest(requestTopic, responseTopic, request.CorrelationId, nameof(GetModbusPayload), payload);

            _pendingReadRequests[request.CorrelationId] = new PendingReadRequest(logicBlockContractId, request.Callback, request.CreatedAt, request.ExpiresAt);
            LogAddedPendingReadRequest(logicBlockContractId, request.ExpiresAt, _pendingReadRequests.Count, request.CorrelationId);
        }

        private void PublishResponse(LogicBlockContractId logicBlockContractId, byte[]? data, Exception? exception, Action<byte[]?, Exception?> callback, Guid correlationId)
        {
            if (!_actorReferences.TryGetValue(logicBlockContractId, out var actorReference))
            {
                if (exception == null)
                {
                    LogSuccessResponseDropped(logicBlockContractId, correlationId);
                }
                else
                {
                    LogErrorResponseDropped(logicBlockContractId, correlationId, exception);
                }

                return;
            }

            LogSendingReadResponseToLogicBlock(logicBlockContractId, correlationId);
            var responseMessage = new ReadModbusRtuResponse(data, exception, callback, correlationId);
            ActorContext.SendTo(actorReference, new ContractMessage<ReadModbusRtuResponse>(logicBlockContractId, responseMessage));
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
                $"{MqttConfiguration.InstallationTopic}/{serviceProviderContractId.ServiceProviderIdentifier}/{serviceProviderContractId.ServiceIdentifier}/{serviceProviderContractId.ContractIdentifier}{Topics.ModbusGet}";
        }

        private static byte[] CreateGetModbusPayload(ModbusFunctionCode functionCode, byte unitId, ushort startingAddress, ushort quantity)
        {
            var flatBufferBuilder = new FlatBufferBuilder(32);
            var payloadOffset = GetModbusPayload.CreateGetModbusPayload(flatBufferBuilder, functionCode, unitId, startingAddress, quantity);
            GetModbusPayload.FinishGetModbusPayloadBuffer(flatBufferBuilder, payloadOffset);

            return flatBufferBuilder.SizedByteArray();
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Received read response (LogicBlockContractId={LogicBlockContractId}, CreatedAt={CreatedAt}, ElapsedMs={ElapsedMs}, CorrelationId={CorrelationId})")]
        private partial void LogReceivedReadResponse(LogicBlockContractId logicBlockContractId, DateTime createdAt, double elapsedMs, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No pending read request found for correlation ID — request probably expired (CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogNoPendingReadRequestFound(Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received read request (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogReceivedReadRequest(LogicBlockContractId logicBlockContractId, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Added pending read request (LogicBlockContractId={LogicBlockContractId}, ExpiresAt={ExpiresAt}, PendingRequests={PendingRequests}, CorrelationId={CorrelationId})")]
        private partial void LogAddedPendingReadRequest(LogicBlockContractId logicBlockContractId, DateTime expiresAt, int pendingRequests, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending read response to logic block (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogSendingReadResponseToLogicBlock(LogicBlockContractId logicBlockContractId, Guid correlationId);

        #endregion

        #region Write

        private void HandleSetResponse(ServiceProviderMqttMessage message)
        {
            var correlationId = message.CorrelationId;
            if (!_pendingWriteRequests.Remove(correlationId, out var pendingRequest))
            {
                LogNoPendingWriteRequestFound(correlationId, message.Topic);
                return;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var elapsedMs = _dateTimeProvider.GetElapsedTime(pendingRequest.CreatedAt).TotalMilliseconds;
                LogReceivedWriteResponse(pendingRequest.LogicBlockContractId, pendingRequest.CreatedAt, elapsedMs, correlationId);
            }

            try
            {
                var payload = SetModbusResponsePayload.GetRootAsSetModbusResponsePayload(message.GetFlatBufferPayload());
                if (payload.ResponseCode == ModbusResponseCode.Ok)
                {
                    PublishResponse(pendingRequest.LogicBlockContractId, null, pendingRequest.Callback, correlationId);
                }
                else
                {
                    var modbusException = new ModbusException((ModbusExceptionCode)payload.ResponseCode, payload.ErrorMessage ?? string.Empty);
                    PublishResponse(pendingRequest.LogicBlockContractId, modbusException, pendingRequest.Callback, correlationId);
                }
            }
            catch (Exception exception)
            {
                PublishResponse(pendingRequest.LogicBlockContractId, exception, pendingRequest.Callback, correlationId);
            }
        }

        private void PublishModbusSetRequest(ContractMessage<WriteModbusRtuRequest> message)
        {
            var logicBlockContractId = message.LogicBlockContractId;
            var request = message.Data;
            LogReceivedWriteRequest(logicBlockContractId, request.CorrelationId);

            var limitReached = CheckPendingRequestLimit();
            if (limitReached)
            {
                PublishResponse(logicBlockContractId, _requestLimitException, request.Callback, request.CorrelationId);
                return;
            }

            if (!_serviceProviderContractIds.TryGetValue(logicBlockContractId, out var serviceProviderContractId))
            {
                PublishResponse(logicBlockContractId, new ServiceProviderContractMappingNotFoundException(logicBlockContractId), request.Callback, request.CorrelationId);
                return;
            }

            if (_dateTimeProvider.UtcNow >= request.ExpiresAt)
            {
                PublishResponse(logicBlockContractId, _timeoutException, request.Callback, request.CorrelationId);
                return;
            }

            var requestTopic = GetOrAddSetTopic(serviceProviderContractId);
            var responseTopic = GetOrAddSetResponseTopic(serviceProviderContractId);
            var payload = CreateSetModbusPayload(request.FunctionCode, request.UnitId, request.Address, request.Data);
            PublishRequest(requestTopic, responseTopic, request.CorrelationId, nameof(SetModbusPayload), payload);

            _pendingWriteRequests[request.CorrelationId] = new PendingWriteRequest(logicBlockContractId, request.Callback, request.CreatedAt, request.ExpiresAt);
            LogAddedPendingWriteRequest(logicBlockContractId, request.ExpiresAt, _pendingWriteRequests.Count, request.CorrelationId);
        }

        private void PublishResponse(LogicBlockContractId logicBlockContractId, Exception? exception, Action<Exception?> callback, Guid correlationId)
        {
            if (!_actorReferences.TryGetValue(logicBlockContractId, out var actorReference))
            {
                if (exception == null)
                {
                    LogSuccessResponseDropped(logicBlockContractId, correlationId);
                }
                else
                {
                    LogErrorResponseDropped(logicBlockContractId, correlationId, exception);
                }

                return;
            }

            LogSendingWriteResponseToLogicBlock(logicBlockContractId, correlationId);
            var responseMessage = new WriteModbusRtuResponse(exception, callback, correlationId);
            ActorContext.SendTo(actorReference, new ContractMessage<WriteModbusRtuResponse>(logicBlockContractId, responseMessage));
        }

        private string GetOrAddSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_setTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = CreateSetTopic(serviceProviderContractId);
                _setTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private string GetOrAddSetResponseTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_setResponseTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = $"{CreateSetTopic(serviceProviderContractId)}/{ServiceProviderConstants.DaleIdentifier}/response";
                _setResponseTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private static string CreateSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            return
                $"{MqttConfiguration.InstallationTopic}/{serviceProviderContractId.ServiceProviderIdentifier}/{serviceProviderContractId.ServiceIdentifier}/{serviceProviderContractId.ContractIdentifier}{Topics.ModbusSet}";
        }

        private static byte[] CreateSetModbusPayload(ModbusFunctionCode functionCode, byte unitId, ushort address, byte[] data)
        {
            var flatBufferBuilder = new FlatBufferBuilder(36 + data.Length);
            var vectorOffset = SetModbusPayload.CreateDataVector(flatBufferBuilder, data);
            var payloadOffset = SetModbusPayload.CreateSetModbusPayload(flatBufferBuilder, functionCode, unitId, address, vectorOffset);
            SetModbusPayload.FinishSetModbusPayloadBuffer(flatBufferBuilder, payloadOffset);

            return flatBufferBuilder.SizedByteArray();
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Received write response (LogicBlockContractId={LogicBlockContractId}, CreatedAt={CreatedAt}, ElapsedMs={ElapsedMs}, CorrelationId={CorrelationId})")]
        private partial void LogReceivedWriteResponse(LogicBlockContractId logicBlockContractId, DateTime createdAt, double elapsedMs, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No pending write request found for correlation ID — request probably expired (CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogNoPendingWriteRequestFound(Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received write request (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogReceivedWriteRequest(LogicBlockContractId logicBlockContractId, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Added pending write request (LogicBlockContractId={LogicBlockContractId}, ExpiresAt={ExpiresAt}, PendingRequests={PendingRequests}, CorrelationId={CorrelationId})")]
        private partial void LogAddedPendingWriteRequest(LogicBlockContractId logicBlockContractId, DateTime expiresAt, int pendingRequests, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending write response to logic block (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogSendingWriteResponseToLogicBlock(LogicBlockContractId logicBlockContractId, Guid correlationId);

        #endregion

        private void PublishRequest(string requestTopic, string responseTopic, Guid correlationId, string schemaName, byte[] payload)
        {
            LogPublishingRequest(correlationId, requestTopic, responseTopic);
            Publish(requestTopic, payload, schemaName, correlationId: correlationId, responseTopic: responseTopic);
        }

        private bool CheckPendingRequestLimit()
        {
            var limitReached = _pendingReadRequests.Count + _pendingWriteRequests.Count >= MaxPendingRequests;
            switch (limitReached)
            {
                case true when !_requestLimitWasReached: LogPendingRequestsLimitReached(MaxPendingRequests); break;
                case false when _requestLimitWasReached: LogPendingRequestsDroppedBelowLimit(_pendingReadRequests.Count + _pendingWriteRequests.Count, MaxPendingRequests); break;
            }

            _requestLimitWasReached = limitReached;

            return limitReached;
        }

        private readonly record struct PendingReadRequest(LogicBlockContractId LogicBlockContractId, Action<byte[]?, Exception?> Callback, DateTime CreatedAt, DateTime ExpiresAt);

        private readonly record struct PendingWriteRequest(LogicBlockContractId LogicBlockContractId, Action<Exception?> Callback, DateTime CreatedAt, DateTime ExpiresAt);

        [LoggerMessage(Level = LogLevel.Error, Message = "Received MQTT message with missing or invalid correlation ID (Topic={Topic})")]
        private partial void LogInvalidCorrelationId(string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Error response dropped — no actor reference found for contract — cannot send response to logic block " +
                                 "(LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogErrorResponseDropped(LogicBlockContractId logicBlockContractId, Guid correlationId, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Success response dropped — no actor reference found for contract — cannot send response to logic block " +
                                 "(LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogSuccessResponseDropped(LogicBlockContractId logicBlockContractId, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Pending requests limit reached — cannot add new requests (PendingRequestLimit={PendingRequestLimit})")]
        private partial void LogPendingRequestsLimitReached(int pendingRequestLimit);

        [LoggerMessage(Level = LogLevel.Information,
                       Message = "Pending requests dropped below limit (PendingRequests={PendingRequests}, PendingRequestLimit={PendingRequestLimit})")]
        private partial void LogPendingRequestsDroppedBelowLimit(int pendingRequests, int pendingRequestLimit);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Retrieved topic from cache " +
                                 "(InstallationTopic={InstallationTopic}, StaticTopicSegment={StaticTopicSegment}, ServiceProviderIdentifier={ServiceProviderIdentifier}, " +
                                 "ServiceIdentifier={ServiceIdentifier}, ContractIdentifier={ContractIdentifier})")]
        private partial void LogRetrievedTopicFromCache(string installationTopic,
                                                        string staticTopicSegment,
                                                        string serviceProviderIdentifier,
                                                        string serviceIdentifier,
                                                        string contractIdentifier);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Added topic to cache " +
                                 "(InstallationTopic={InstallationTopic}, StaticTopicSegment={StaticTopicSegment}, ServiceProviderIdentifier={ServiceProviderIdentifier}, " +
                                 "ServiceIdentifier={ServiceIdentifier}, ContractIdentifier={ContractIdentifier})")]
        private partial void LogAddedTopicToCache(string installationTopic,
                                                  string staticTopicSegment,
                                                  string serviceProviderIdentifier,
                                                  string serviceIdentifier,
                                                  string contractIdentifier);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Retrieved topic from cache " +
                                 "(InstallationTopic={InstallationTopic}, StaticTopicSegment={StaticTopicSegment}, ServiceProviderIdentifier={ServiceProviderIdentifier}, " +
                                 "ServiceIdentifier={ServiceIdentifier}, ContractIdentifier={ContractIdentifier}, ClientId={ClientId})")]
        private partial void LogRetrievedTopicFromCache(string installationTopic,
                                                        string staticTopicSegment,
                                                        string serviceProviderIdentifier,
                                                        string serviceIdentifier,
                                                        string contractIdentifier,
                                                        string clientId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Added topic to cache (InstallationTopic={InstallationTopic}, StaticTopicSegment={StaticTopicSegment}, ServiceProviderIdentifier={ServiceProviderIdentifier}, " +
                           "ServiceIdentifier={ServiceIdentifier}, ContractIdentifier={ContractIdentifier}, ClientId={ClientId})")]
        private partial void LogAddedTopicToCache(string installationTopic,
                                                  string staticTopicSegment,
                                                  string serviceProviderIdentifier,
                                                  string serviceIdentifier,
                                                  string contractIdentifier,
                                                  string clientId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing request (RequestTopic={RequestTopic}, ResponseTopic={ResponseTopic}, CorrelationId={CorrelationId})")]
        private partial void LogPublishingRequest(Guid correlationId, string requestTopic, string responseTopic);

        #endregion

        #region RequestExpiration

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
            if (_pendingWriteRequests.Count == 0 && _pendingReadRequests.Count == 0)
            {
                return;
            }

            LogCheckingForExpiredRequests(_pendingReadRequests.Count, _pendingWriteRequests.Count);
            var utcNow = _dateTimeProvider.UtcNow;
            foreach (var (correlationId, pendingRequest) in _pendingReadRequests)
            {
                if (utcNow < pendingRequest.ExpiresAt)
                {
                    continue;
                }

                LogCompletingExpiredReadRequest(pendingRequest.CreatedAt, pendingRequest.ExpiresAt, correlationId, pendingRequest.LogicBlockContractId);
                _pendingReadRequests.Remove(correlationId);
                PublishResponse(pendingRequest.LogicBlockContractId, null, _timeoutException, pendingRequest.Callback, correlationId);
            }

            foreach (var (correlationId, pendingRequest) in _pendingWriteRequests)
            {
                if (utcNow < pendingRequest.ExpiresAt)
                {
                    continue;
                }

                LogCompletingExpiredWriteRequest(pendingRequest.CreatedAt, pendingRequest.ExpiresAt, correlationId, pendingRequest.LogicBlockContractId);
                _pendingWriteRequests.Remove(correlationId);
                PublishResponse(pendingRequest.LogicBlockContractId, _timeoutException, pendingRequest.Callback, correlationId);
            }
        }

        private void ScheduleCheckExpiredRequests()
        {
            LogSendingCheckExpiredRequests(_checkExpiredRequestsDelay.TotalSeconds);
            InvokeSynchronizedAfter(CompleteExpiredRequests, _checkExpiredRequestsDelay);
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Checking for expired requests (PendingReadRequests={PendingReadRequests}, PendingWriteRequests={PendingWriteRequests})")]
        private partial void LogCheckingForExpiredRequests(int pendingReadRequests, int pendingWriteRequests);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Completing expired read request, created at {CreatedAt}, should have expired at {ExpiresAt} (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogCompletingExpiredReadRequest(DateTime createdAt, DateTime expiresAt, Guid correlationId, LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Completing expired write request, created at {CreatedAt}, should have expired at {ExpiresAt} (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogCompletingExpiredWriteRequest(DateTime createdAt, DateTime expiresAt, Guid correlationId, LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduling check expired requests in {DelaySeconds} seconds")]
        private partial void LogSendingCheckExpiredRequests(double delaySeconds);

        #endregion
    }

    /// <summary>
    ///     The exception that is thrown when the pending requests limit has been reached.
    /// </summary>
    public class PendingRequestsLimitReachedException : InvalidOperationException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PendingRequestsLimitReachedException" /> class.
        /// </summary>
        /// <param name="maxPendingRequests">The maximum number of pending requests allowed.</param>
        public PendingRequestsLimitReachedException(int maxPendingRequests) : base($"The pending requests limit of {maxPendingRequests} has been reached.")
        {
        }
    }

    /// <summary>
    ///     The exception that is thrown when no service provider contract mapping is found for a contract.
    /// </summary>
    public class ServiceProviderContractMappingNotFoundException : KeyNotFoundException
    {
        /// <summary>
        ///     Gets the LogicBlockContractId for which no mapping was found.
        /// </summary>
        public LogicBlockContractId LogicBlockContractId { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceProviderContractMappingNotFoundException" /> class.
        /// </summary>
        /// <param name="logicBlockContractId">The contract ID for which no mapping was found.</param>
        public ServiceProviderContractMappingNotFoundException(LogicBlockContractId logicBlockContractId) :
            base($"No service provider contract mapping found for '{logicBlockContractId}'.")
        {
            LogicBlockContractId = logicBlockContractId;
        }
    }
}