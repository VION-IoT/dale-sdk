using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     Drives a logic block through the actor messages the runtime sends, so the config-gating suite
    ///     observes a configured block rather than a hand-assembled one.
    ///     <para>
    ///         The bound member set is read back from the <see cref="BindLogicBlockServices" /> message the
    ///         block emits at the end of initialization, which is why <see cref="Configure" /> takes the
    ///         block's <b>maximum</b> service set: the block maps only the services it actually bound, so the
    ///         emitted keys are the answer. That message is the seam replacing reflection into the block's
    ///         private binder (<c>testing-conventions.md</c> section 7).
    ///     </para>
    /// </summary>
    public sealed class GatingHarness
    {
        private readonly Mock<IActorContext> _actorContextMock = new();

        private readonly List<object> _responses = [];

        private readonly List<object> _sentMessages = [];

        public IActorContext Context
        {
            get => _actorContextMock.Object;
        }

        /// <summary>The service provider a block is initialized with — loggers only, the bare-host shape.</summary>
        public static IServiceProvider ServiceProvider { get; } = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                         .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                         .BuildServiceProvider();

        /// <summary>The responses the block sent back to the requester, oldest first.</summary>
        public IReadOnlyList<object> Responses
        {
            get => _responses;
        }

        public GatingHarness()
        {
            _actorContextMock.Setup(c => c.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()))
                             .Callback<IActorReference, object, Dictionary<string, string>?>((_, message, _) => _sentMessages.Add(message));
            _actorContextMock.Setup(c => c.RespondToSender(It.IsAny<object>())).Callback<object>(_responses.Add);
            _actorContextMock.Setup(c => c.LookupByName(It.IsAny<string>())).Returns(new Mock<IActorReference>().Object);
        }

        /// <summary>
        ///     Links the runtime actors and then configures <paramref name="block" /> with
        ///     <paramref name="parameters" />, exactly as the runtime does. <paramref name="serviceIdentifiers" />
        ///     is the block's maximum service set; the bound subset comes back from <see cref="BoundServices" />.
        /// </summary>
        public void Configure(LogicBlockBase block, IEnumerable<string> serviceIdentifiers, params SetLogicConfigurationPayload.InstantiationParameterValue[] parameters)
        {
            Link(block);
            Send(block, Initialize(serviceIdentifiers, parameters));
        }

        /// <summary>Builds a configuration message without sending it, for tests that drive a second one.</summary>
        public static InitializeLogicBlock Initialize(IEnumerable<string> serviceIdentifiers, params SetLogicConfigurationPayload.InstantiationParameterValue[] parameters)
        {
            return Initialize(serviceIdentifiers, [], parameters);
        }

        /// <summary>Builds a configuration message that also maps <paramref name="contractIdentifiers" /> to contracts.</summary>
        public static InitializeLogicBlock Initialize(IEnumerable<string> serviceIdentifiers,
                                                      IEnumerable<string> contractIdentifiers,
                                                      params SetLogicConfigurationPayload.InstantiationParameterValue[] parameters)
        {
            return new InitializeLogicBlock("cfg",
                                            "block",
                                            serviceIdentifiers.ToDictionary(identifier => identifier, identifier => new ServiceIdentifier(identifier)),
                                            contractIdentifiers.ToDictionary(identifier => identifier, identifier => new LogicBlockContractId("cfg", identifier)),
                                            ServiceProvider,
                                            parameters.Length > 0 ? parameters.ToList() : null);
        }

        /// <summary>
        ///     The keys of the block's persistent-data snapshot, taken through the runtime's own teardown
        ///     sequence — the snapshot is created on stop and read by the request that follows it.
        /// </summary>
        public IReadOnlyCollection<string> SnapshotKeys(LogicBlockBase block)
        {
            Send(block, new StopLogicBlockRequest());
            Send(block, new GetPersistentDataSnapshotRequest());

            return _responses.OfType<GetPersistentDataSnapshotResponse>().Last().PersistentDataValues.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        }

        public void Link(LogicBlockBase block)
        {
            Send(block,
                 new LinkRuntimeActors
                 {
                     ServicePropertyHandlerActor = Context.LookupByName("ServicePropertyHandler"),
                     ServiceMeasuringPointHandlerActor = Context.LookupByName("ServiceMeasuringPointHandler"),
                     PersistenceManagerActor = Context.LookupByName("PersistentDataHandler"),
                 });
        }

        public void Send(LogicBlockBase block, object message)
        {
            block.HandleMessageAsync(message, Context).GetAwaiter().GetResult();
        }

        /// <summary>The service identifiers the block actually bound, from the binding announcement it emitted.</summary>
        public IReadOnlyCollection<string> BoundServices()
        {
            var announcement = _sentMessages.OfType<BindLogicBlockServices>().Last();
            return announcement.Properties.Keys.Concat(announcement.MeasuringPoints.Keys).Select(identifier => identifier.Id).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>The service-property identifiers the block bound under <paramref name="serviceIdentifier" />.</summary>
        public IReadOnlyCollection<string> BoundProperties(string serviceIdentifier)
        {
            var announcement = _sentMessages.OfType<BindLogicBlockServices>().Last();
            return announcement.Properties.TryGetValue(new ServiceIdentifier(serviceIdentifier), out var properties) ? properties.Keys.ToHashSet(StringComparer.Ordinal) : [];
        }

        /// <summary>The measuring-point identifiers the block bound under <paramref name="serviceIdentifier" />.</summary>
        public IReadOnlyCollection<string> BoundMeasuringPoints(string serviceIdentifier)
        {
            var announcement = _sentMessages.OfType<BindLogicBlockServices>().Last();
            return announcement.MeasuringPoints.TryGetValue(new ServiceIdentifier(serviceIdentifier), out var points) ? points.Keys.ToHashSet(StringComparer.Ordinal) : [];
        }
    }
}