using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Contracts.Constants;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.MBus.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     The shared handler between the M-Bus contracts and the service providers, driven through its actor messages
    ///     with a fake clock; the MQTT client and the logic block are mocked actor references.
    /// </summary>
    [TestClass]
    public class MBusHandlerShould
    {
        private const string ContractIdentifier = "mbus0";

        private const string ServiceProviderIdentifier = "sp0";

        private const string ServiceIdentifier = "svc0";

        private const byte PrimaryAddress = 1;

        private const int MaxTelegrams = 5;

        private const string GetRequestTopic = $"installation/{ServiceProviderIdentifier}/{ServiceIdentifier}/{ContractIdentifier}/hw/mbus/get";

        private const string GetResponseTopic = $"{GetRequestTopic}/{ServiceProviderConstants.DaleIdentifier}/response";

        private static readonly LogicBlockContractId LinkedLogicBlockContractId = new(new LogicBlockId("lb0"), ContractIdentifier);

        private static readonly LogicBlockContractId UnlinkedLogicBlockContractId = new(new LogicBlockId("lb-unlinked"), ContractIdentifier);

        private static readonly ServiceProviderContractId SpContractId = new(ServiceProviderIdentifier, ServiceIdentifier, ContractIdentifier);

        private static readonly DateTime Now = new(2026,
                                                   1,
                                                   1,
                                                   0,
                                                   0,
                                                   0,
                                                   DateTimeKind.Utc);

        private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan MaxQueuedAge = TimeSpan.FromSeconds(30);

        private readonly Mock<IActorContext> _actorContextMock = new();

        private readonly Mock<IActorReference> _logicBlockActorRefMock = new();

        private readonly Mock<IActorReference> _mqttClientActorRefMock = new();

        private readonly FakeTimeProvider _timeProvider = new(Now);

        private object? _scheduledSweep;

        private TimeSpan? _scheduledSweepDelay;

        private MBusHandler _sut = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            MqttConfiguration.InstallationTopic = "installation";
            _actorContextMock.Setup(actorContext => actorContext.LookupByName(MqttConstants.MqttClientName)).Returns(_mqttClientActorRefMock.Object);
            _actorContextMock.Setup(actorContext => actorContext.SendToSelfAfter(It.IsAny<object>(), It.IsAny<TimeSpan>()))
                             .Callback<object, TimeSpan>((message, delay) =>
                                                         {
                                                             _scheduledSweep = message;
                                                             _scheduledSweepDelay = delay;
                                                         });
            _sut = new MBusHandler(_timeProvider, new Mock<ILogger<MBusHandler>>().Object);
            await DispatchAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                 {
                                                                     [SpContractId] = new() { [LinkedLogicBlockContractId] = _logicBlockActorRefMock.Object },
                                                                 }));
            _actorContextMock.Invocations.Clear();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.1")]
        public async Task PublishReadAsGetMBusPayload()
        {
            // Arrange

            // Act
            await SendReadAsync(Guid.NewGuid());

            // Assert
            var published = CapturedPublish();
            Assert.AreEqual(GetRequestTopic, published.Topic);
            Assert.AreEqual(GetResponseTopic, published.ResponseTopic);
            Assert.AreEqual("""{"primaryAddress":1,"maxTelegrams":5}""", Encoding.UTF8.GetString(published.Payload!));
            Assert.AreEqual(MessageMimeTypes.Json, published.ContentType);
            Assert.AreEqual("GetMBusPayload", published.UserProperties!.Single(property => property.Name == MqttUserProperties.Schema.Name).Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.6")]
        public async Task PublishDeadlineOfPublishPlusTimeout()
        {
            // Arrange
            // The read was issued ten seconds before the handler takes it; the deadline counts from the publish.
            var createdAt = Now;
            _timeProvider.Advance(TimeSpan.FromSeconds(10));

            // Act
            await SendReadAsync(Guid.NewGuid(), createdAt: createdAt);

            // Assert
            Assert.AreEqual(new DateTimeOffset(Now.AddSeconds(10) + OperationTimeout), CapturedPublish().Deadline);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.2")]
        public async Task HandTelegramsOfOkAnswerToLogicBlock()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            await SendReadAsync(correlationId);

            // Act
            await DispatchAsync(Answer(correlationId, "Ok", telegrams: [MBusVectors.Em530Telegram1, MBusVectors.Em530Telegram2]));

            // Assert
            var response = CapturedResponse();
            Assert.AreEqual(MBusOutcome.Success, response.Outcome);
            Assert.IsNull(response.Exception);
            Assert.AreEqual(MBusVectors.Em530Telegram1 + "|" + MBusVectors.Em530Telegram2, string.Join("|", response.Telegrams!.Select(MBusFrames.Hex)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.3")]
        [DataRow("NoResponse", MBusOutcome.NoResponse)]
        [DataRow("Garbled", MBusOutcome.Garbled)]
        [DataRow("LineUnavailable", MBusOutcome.LineUnavailable)]
        [DataRow("NotSupported", MBusOutcome.Invalid)]
        public async Task MapResponseCodeToOutcome(string responseCode, MBusOutcome expected)
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            await SendReadAsync(correlationId);

            // Act
            await DispatchAsync(Answer(correlationId, responseCode, "no answer after 2 repetitions"));

            // Assert
            var response = CapturedResponse();
            Assert.AreEqual(expected, response.Outcome);
            Assert.AreEqual(expected, response.Exception!.Outcome);
            Assert.AreEqual(responseCode, response.Exception.ResponseCode);
            Assert.IsNull(response.Telegrams);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.8")]
        public async Task ReadUnknownResponseCodeAsInvalid()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            await SendReadAsync(correlationId);

            // Act
            await DispatchAsync(Answer(correlationId, "QueueFull"));

            // Assert
            var response = CapturedResponse();
            Assert.AreEqual(MBusOutcome.Invalid, response.Outcome);
            Assert.AreEqual("QueueFull", response.Exception!.ResponseCode);
        }

        [TestMethod]
        [DataRow("""{"errorMessage":null}""", DisplayName = "no response code")]
        [DataRow("""{"responseCode":"Ok","telegrams":["not base64!"]}""", DisplayName = "telegram not base64")]
        [DataRow("not json", DisplayName = "not JSON")]
        public async Task CompleteUnreadableAnswerAsProtocolError(string document)
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            await SendReadAsync(correlationId);

            // Act
            await DispatchAsync(Message(Encoding.UTF8.GetBytes(document), correlationId));

            // Assert
            Assert.AreEqual(MBusOutcome.ProtocolError, CapturedResponse().Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.4")]
        public async Task CompleteUnmappedReadAsInvalid()
        {
            // Arrange
            // The contract was linked by an earlier map and left out of this one, so its actor is still known.
            await DispatchAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                 {
                                                                     [SpContractId] = new() { [UnlinkedLogicBlockContractId] = _logicBlockActorRefMock.Object },
                                                                 }));
            await DispatchAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                 {
                                                                     [SpContractId] = new() { [LinkedLogicBlockContractId] = _logicBlockActorRefMock.Object },
                                                                 }));
            _actorContextMock.Invocations.Clear();

            // Act
            await SendReadAsync(Guid.NewGuid(), UnlinkedLogicBlockContractId);

            // Assert
            Assert.AreEqual(MBusOutcome.Invalid, CapturedResponse().Outcome);
            VerifyNothingPublished();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.5")]
        public async Task IgnoreResponseWithoutPendingRead()
        {
            // Arrange
            await SendReadAsync(Guid.NewGuid());
            _actorContextMock.Invocations.Clear();

            // Act
            await DispatchAsync(Answer(Guid.NewGuid(), "Ok", telegrams: [MBusVectors.Em530Telegram5]));

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_logicBlockActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.6")]
        public async Task DropReadAtPendingLimit()
        {
            // Arrange
            for (var index = 0; index < MBusHandler.MaxPendingRequests; index++)
            {
                await SendReadAsync(Guid.NewGuid());
            }

            _actorContextMock.Invocations.Clear();

            // Act
            await SendReadAsync(Guid.NewGuid());

            // Assert
            Assert.AreEqual(MBusOutcome.Dropped, CapturedResponse().Outcome);
            VerifyNothingPublished();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.4")]
        [DataRow(30.0, false, DisplayName = "at the maximum queued age")]
        [DataRow(30.001, true, DisplayName = "past the maximum queued age")]
        public async Task ExpireReadOlderThanMaxQueuedAge(double waitedSeconds, bool expired)
        {
            // Arrange
            var createdAt = Now;
            _timeProvider.Advance(TimeSpan.FromSeconds(waitedSeconds));

            // Act
            await SendReadAsync(Guid.NewGuid(), createdAt: createdAt);

            // Assert
            if (expired)
            {
                Assert.AreEqual(MBusOutcome.Expired, CapturedResponse().Outcome);
                VerifyNothingPublished();
            }
            else
            {
                CapturedPublish();
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.5")]
        public async Task CompleteTimedOutReadOnSweep()
        {
            // Arrange
            await SendReadAsync(Guid.NewGuid());
            _timeProvider.Advance(OperationTimeout);

            // Act
            await RunSweepAsync();

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(1), _scheduledSweepDelay);
            Assert.AreEqual(MBusOutcome.Timeout, CapturedResponse().Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.3")]
        [DataRow(-0.001, false, DisplayName = "just before the timeout counted from the publish")]
        [DataRow(0.0, true, DisplayName = "at the timeout counted from the publish")]
        public async Task StartTimeoutAtPublish(double offsetSeconds, bool timedOut)
        {
            // Arrange
            // Issued twenty seconds before the handler took it: counted from the call, the timeout would already be due.
            var createdAt = Now;
            _timeProvider.Advance(TimeSpan.FromSeconds(20));
            await SendReadAsync(Guid.NewGuid(), createdAt: createdAt);
            _actorContextMock.Invocations.Clear();
            _timeProvider.Advance(OperationTimeout + TimeSpan.FromSeconds(offsetSeconds));

            // Act
            await RunSweepAsync();

            // Assert
            var sent = _actorContextMock.Invocations.Count(invocation => invocation.Method.Name == nameof(IActorContext.SendTo) &&
                                                                         ReferenceEquals(invocation.Arguments[0], _logicBlockActorRefMock.Object));
            Assert.AreEqual(timedOut ? 1 : 0, sent);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.3")]
        public async Task ReportQueuedWaitOverHopToHandler()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            var createdAt = Now;
            _timeProvider.Advance(TimeSpan.FromSeconds(4));
            await SendReadAsync(correlationId, createdAt: createdAt);
            _timeProvider.Advance(TimeSpan.FromSeconds(7));

            // Act
            await DispatchAsync(Answer(correlationId, "NoResponse"));

            // Assert
            var receipt = CapturedResponse().ToReceipt();
            Assert.AreEqual(TimeSpan.FromSeconds(4), receipt.QueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(7), receipt.RoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.5")]
        public async Task RenderExpiryInInvariantCulture()
        {
            // Arrange
            var createdAt = Now;
            _timeProvider.Advance(TimeSpan.FromSeconds(30.5));
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            try
            {
                // Act
                await SendReadAsync(Guid.NewGuid(), createdAt: createdAt);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            // Assert
            Assert.AreEqual("The read waited 30.5 s to reach the M-Bus handler, longer than its maximum queued age of 30 s, so it was not published.",
                            CapturedResponse().Exception!.Message);
        }

        private Task DispatchAsync(object message)
        {
            return ((IActorReceiver)_sut).HandleMessageAsync(message, _actorContextMock.Object);
        }

        private Task SendReadAsync(Guid correlationId, LogicBlockContractId? logicBlockContractId = null, DateTime? createdAt = null)
        {
            var request = new ReadMBusRequest(PrimaryAddress,
                                              MaxTelegrams,
                                              createdAt ?? Now,
                                              OperationTimeout,
                                              MaxQueuedAge,
                                              correlationId,
                                              (_, _, _) => { });

            return DispatchAsync(new ContractMessage<ReadMBusRequest>(logicBlockContractId ?? LinkedLogicBlockContractId, request));
        }

        private Task RunSweepAsync()
        {
            var sweep = _scheduledSweep!;
            _scheduledSweep = null;
            return DispatchAsync(sweep);
        }

        private PublishMqttMessage CapturedPublish()
        {
            var published = _actorContextMock.Invocations
                                             .Where(invocation => invocation.Method.Name == nameof(IActorContext.SendTo) &&
                                                                  ReferenceEquals(invocation.Arguments[0], _mqttClientActorRefMock.Object))
                                             .Select(invocation => invocation.Arguments[1])
                                             .OfType<PublishMqttMessage>()
                                             .ToList();
            Assert.HasCount(1, published);
            return published[0];
        }

        private ReadMBusResponse CapturedResponse()
        {
            var responses = _actorContextMock.Invocations
                                             .Where(invocation => invocation.Method.Name == nameof(IActorContext.SendTo) &&
                                                                  ReferenceEquals(invocation.Arguments[0], _logicBlockActorRefMock.Object))
                                             .Select(invocation => invocation.Arguments[1])
                                             .OfType<ContractMessage<ReadMBusResponse>>()
                                             .ToList();
            Assert.HasCount(1, responses);
            return responses[0].Data;
        }

        private void VerifyNothingPublished()
        {
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        /*
         * Written as the literal JSON a provider puts on the wire, not serialized through the context the handler
         * decodes with, so a handler reading another naming policy or a numeric code fails here.
         */
        private static MqttMessageReceived Answer(Guid correlationId, string responseCode, string? errorMessage = null, string[]? telegrams = null)
        {
            var telegramsJson = telegrams == null ? "null" : $"[{string.Join(",", telegrams.Select(hex => $"\"{Convert.ToBase64String(MBusFrames.Bytes(hex))}\""))}]";
            var errorJson = errorMessage == null ? "null" : $"\"{errorMessage}\"";
            var document = $$"""{"responseCode":"{{responseCode}}","errorMessage":{{errorJson}},"telegrams":{{telegramsJson}}}""";

            return Message(Encoding.UTF8.GetBytes(document), correlationId);
        }

        private static MqttMessageReceived Message(byte[] payload, Guid correlationId)
        {
            return new MqttMessageReceived(GetResponseTopic, new ReadOnlySequence<byte>(payload), correlationId.ToByteArray(), null, []);
        }
    }
}