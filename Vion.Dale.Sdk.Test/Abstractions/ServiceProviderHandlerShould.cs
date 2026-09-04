using System;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     What the base gives an author of a provider face and what it demands of one: the five ends its
    ///     sealed dispatch routes to, the registration it performs on their behalf, and the two lookups every
    ///     shipped handler is built on. The handler is driven through its own message loop against a recording
    ///     actor context, which is where a handler's whole outward behaviour is observable.
    /// </summary>
    [TestClass]
    public class ServiceProviderHandlerShould
    {
        private static readonly LogicBlockContractId BlockContract = new(new LogicBlockId("block-1"), "probe");

        private static readonly ServiceProviderContractId ProviderContract = new("sp", "svc", "c1");

        private readonly LifecycleHarness.RecordingActorContext _context = new();

        private readonly BindProbeHandler _sut = new();

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.1")]
        public void IgnoreMessageOfUnknownKind()
        {
            // Arrange / Act
            Send("a message the base does not route");

            // Assert
            Assert.IsEmpty(_context.Log);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.1")]
        [TestProperty("spec", "AC-BIND-010.2")]
        public void RouteMqttMessageToSubclass()
        {
            // Arrange
            MqttTopics.Configure();

            // Act
            Send(new MqttMessageReceived($"{MqttConfiguration.InstallationTopic}/sp/svc/c1/state", default, null, null, []));

            // Assert
            Assert.AreEqual(ProviderContract, _sut.ReceivedMqttMessages.Single().ContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.1")]
        [TestProperty("spec", "AC-BIND-010.2")]
        public void RouteContractMessageToSubclass()
        {
            // Arrange / Act
            Send(new ContractMessage<PokeBindProbe>(BlockContract, new PokeBindProbe(3)));

            // Assert
            Assert.AreEqual(BlockContract, _sut.ReceivedContractMessages.Single().LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.3")]
        public void SubscribeDeclaredActionPathUnderServiceProviderWildcard()
        {
            // Arrange / Act
            Send(new RegisterMqttHandlerRequest());

            // Assert
            var registration = Registration();
            CollectionAssert.AreEqual(new[] { "/+/+/+/state" }, registration.TopicGroups.Single().Topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.3")]
        public void RegisterUnderHandlerClassNameAndDeclaredRoutingKey()
        {
            // Arrange / Act
            Send(new RegisterMqttHandlerRequest());

            // Assert
            var registration = Registration();
            Assert.AreEqual(nameof(BindProbeHandler), registration.HandlerName);
            Assert.AreEqual("probe", registration.TopicRoutingKey);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.3")]
        public void AnswerRegistrationRequest()
        {
            // Arrange / Act
            Send(new RegisterMqttHandlerRequest());

            // Assert
            Assert.IsInstanceOfType<RegisterMqttHandlerResponse>(_context.Responses.Single());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.4")]
        public void LeaveActionPathWithoutSeparatorOutOfRegistration()
        {
            // Arrange
            var handler = new BindProbeHandler(actionPaths: ["state", "/other"]);

            // Act
            Send(new RegisterMqttHandlerRequest(), handler);

            // Assert
            CollectionAssert.AreEqual(new[] { "/+/+/+/other" }, Registration().TopicGroups.Single().Topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.4")]
        public void AnswerRegistrationWhoseActionPathWasSkipped()
        {
            // Arrange
            var handler = new BindProbeHandler(actionPaths: ["state"]);

            // Act
            Send(new RegisterMqttHandlerRequest(), handler);

            // Assert — a runtime waits for this answer on a timeout, so a skipped path must not withhold it.
            Assert.IsInstanceOfType<RegisterMqttHandlerResponse>(_context.Responses.Single());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.5")]
        public void NotifySubclassWhenContractMapArrives()
        {
            // Arrange / Act
            Send(new LinkLogicBlockContractActors(Map()));

            // Assert
            Assert.AreEqual(1, _sut.LinkCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.5")]
        public void ReplaceContractMappingsWhenSecondMapArrives()
        {
            // Arrange
            Send(new LinkLogicBlockContractActors(Map()));

            // Act
            Send(new LinkLogicBlockContractActors([]));

            // Assert
            Assert.IsEmpty(_sut.MappedContractsOf(BlockContract));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.6")]
        public void RefuseActorContextReadBeforeFirstMessage()
        {
            // Arrange / Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => _sut.ReadActorContext());
            StringAssert.Contains(exception.Message, nameof(BindProbeHandler));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.7")]
        public void ForwardValueToEveryMappedLogicBlockContract()
        {
            // Arrange
            Send(new LinkLogicBlockContractActors(Map()));

            // Act
            _sut.ForwardProbe(ProviderContract, 5);

            // Assert
            var forwarded = _context.Sent.Select(sent => sent.Message).OfType<ContractMessage<BindProbeConfirmed>>().Single();
            Assert.AreEqual(BlockContract, forwarded.LogicBlockContractId);
            Assert.AreEqual(5, forwarded.Data.Amount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.7")]
        public void ForwardNothingForUnmappedServiceProviderContract()
        {
            // Arrange
            Send(new LinkLogicBlockContractActors(Map()));

            // Act
            _sut.ForwardProbe(new ServiceProviderContractId("sp", "svc", "absent"), 5);

            // Assert
            Assert.IsEmpty(_context.Sent);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.8")]
        public void ReportServiceProviderContractsMappedToLogicBlockContract()
        {
            // Arrange
            Send(new LinkLogicBlockContractActors(Map()));

            // Act
            var mapped = _sut.MappedContractsOf(BlockContract);

            // Assert
            CollectionAssert.AreEqual(new[] { ProviderContract }, mapped);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.8")]
        public void ReportNoServiceProviderContractForUnmappedLogicBlockContract()
        {
            // Arrange
            Send(new LinkLogicBlockContractActors(Map()));

            // Act
            var mapped = _sut.MappedContractsOf(new LogicBlockContractId(new LogicBlockId("elsewhere"), "probe"));

            // Assert
            Assert.IsEmpty(mapped);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-010.9")]
        public void RunScheduledActionOnHandlersOwnActor()
        {
            // Arrange
            Send(new RegisterMqttHandlerRequest());
            var ran = false;

            // Act
            _sut.ScheduleProbe(() => ran = true, TimeSpan.FromSeconds(3));
            var scheduled = _context.Scheduled.Single();
            Send(scheduled.Message);

            // Assert
            Assert.IsTrue(ran);
            Assert.AreEqual(TimeSpan.FromSeconds(3), scheduled.Delay);
        }

        private Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> Map()
        {
            return new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                   {
                       [ProviderContract] = new()
                                            {
                                                [BlockContract] =
                                                    new LifecycleHarness.NamedReference("block"),
                                            },
                   };
        }

        private RegisterMqttHandler Registration()
        {
            return _context.Sent.Select(sent => sent.Message).OfType<RegisterMqttHandler>().Single();
        }

        private void Send(object message, ServiceProviderHandlerBase? handler = null)
        {
            ((IActorReceiver)(handler ?? _sut)).HandleMessageAsync(message, _context).GetAwaiter().GetResult();
        }
    }
}