using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     The contract this area's suites link across: one declaration carrying all three message kinds, so
    ///     the generated surface a block author meets — a command, a state update and a request with its
    ///     answer — is readable from one place.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "IBindSource",
                        AndInterface = "IBindSink",
                        BetweenDefaultName = "Bind source",
                        AndDefaultName = "Bind sink",
                        Direction = ContractDirection.BetweenToAnd)]
    public static class BindLinkContract
    {
        [Command(From = "IBindSource", To = "IBindSink")]
        public readonly record struct Nudge(int Amount);

        [StateUpdate(From = "IBindSink", To = "IBindSource")]
        public readonly record struct Level(int Value);

        [RequestResponse(From = "IBindSource", To = "IBindSink", ResponseType = typeof(Reading))]
        public readonly record struct Poll(int Channel);

        public readonly record struct Reading(int Value);
    }

    /// <summary>
    ///     A one-way contract, so one role receives and never sends — the shape whose generated surface
    ///     carries no extension class at all.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "IBindTalker", AndInterface = "IBindListener")]
    public static class BindOneWayContract
    {
        [Command(From = "IBindTalker", To = "IBindListener")]
        public readonly record struct Say(string Word);
    }

    /// <summary>A block binding the listening half, which sends nothing.</summary>
    public sealed class BindListenerBlock : LogicBlockBase, IBindListener
    {
        public List<BindOneWayContract.Say> Heard { get; } = [];

        public BindListenerBlock() : base(NullLogger.Instance)
        {
        }

        public void HandleCommand(BindOneWayContract.Say command)
        {
            Heard.Add(command);
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A block binding the talking half.</summary>
    public sealed class BindTalkerBlock : LogicBlockBase, IBindTalker
    {
        public BindTalkerBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A service-provider contract for the suites that bind one.</summary>
    [ServiceProviderContractType("BindProbe", Consumers = LinkMultiplicity.ZeroOrOne)]
    public interface IBindProbe
    {
        event EventHandler<int>? Confirmed;

        void Poke(int amount);
    }

    /// <summary>A provider face, so the development-only declaration has an instance in this project.</summary>
    [ServiceProviderContractType("BindProbeProvider", DevelopmentOnly = true)]
    public interface IBindProbeProvider
    {
        void Confirm(int amount);
    }

    /// <summary>A type carrying no contract marker, for the binding the marker rule cannot reach.</summary>
    public interface IUnmarkedContract
    {
    }

    /// <summary>What a bind probe writes to its handler.</summary>
    public readonly record struct PokeBindProbe(int Amount);

    /// <summary>What a bind probe's handler confirms back.</summary>
    public readonly record struct BindProbeConfirmed(int Amount);

    /// <inheritdoc cref="IBindProbe" />
    public class BindProbeContract : LogicBlockContractBase, IBindProbe
    {
        public override string ContractHandlerActorName { get; protected set; } = nameof(BindProbeHandler);

        public BindProbeContract(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public event EventHandler<int>? Confirmed;

        public void Poke(int amount)
        {
            SendToContractHandler(new ContractMessage<PokeBindProbe>(LogicBlockContractId, new PokeBindProbe(amount)));
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            if (contractMessage is ContractMessage<BindProbeConfirmed> confirmed)
            {
                Confirmed?.Invoke(this, confirmed.Data.Amount);
            }
        }
    }

    /// <inheritdoc cref="IBindProbeProvider" />
    public class BindProbeProviderContract : LogicBlockContractBase, IBindProbeProvider
    {
        public override string ContractHandlerActorName { get; protected set; } = nameof(BindProbeProviderHandler);

        public BindProbeProviderContract(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public void Confirm(int amount)
        {
            SendToContractHandler(new ContractMessage<BindProbeConfirmed>(LogicBlockContractId, new BindProbeConfirmed(amount)));
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }
    }

    /// <summary>The handler a bind probe addresses, driven directly through its dispatch.</summary>
    public class BindProbeHandler : ServiceProviderHandlerBase
    {
        private readonly string[] _actionPaths;

        private readonly string _routingKey;

        public List<ServiceProviderMqttMessage> ReceivedMqttMessages { get; } = [];

        public List<IContractMessage> ReceivedContractMessages { get; } = [];

        public int LinkCount { get; private set; }

        public BindProbeHandler(string routingKey = "probe", string[]? actionPaths = null) : base(NullLogger.Instance)
        {
            _routingKey = routingKey;
            _actionPaths = actionPaths ?? ["/state"];
        }

        /// <summary>Publishes with whatever content type the caller passes, so the default is observable.</summary>
        public Guid PublishProbe(string? contentType = null, Guid? correlationId = null, string? responseTopic = null, bool retain = false)
        {
            return Publish("probe/topic",
                           [1, 2, 3],
                           "ProbeSchema",
                           contentType,
                           correlationId,
                           responseTopic,
                           retain);
        }

        public Guid PublishProbeAsJson(int payload)
        {
            return PublishJson("probe/topic", payload, "ProbeSchema");
        }

        public void ForwardProbe(ServiceProviderContractId contractId, int amount)
        {
            ForwardToLogicBlocks(contractId, new BindProbeConfirmed(amount));
        }

        public List<ServiceProviderContractId> MappedContractsOf(LogicBlockContractId logicBlockContractId)
        {
            return FindMappedServiceProviderContracts(logicBlockContractId);
        }

        public void ScheduleProbe(Action action, TimeSpan delay)
        {
            InvokeSynchronizedAfter(action, delay);
        }

        public IActorContext ReadActorContext()
        {
            return ActorContext;
        }

        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (_routingKey, _actionPaths);
        }

        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            ReceivedMqttMessages.Add(message);
        }

        protected override void HandleContractMessage(IContractMessage message)
        {
            ReceivedContractMessages.Add(message);
        }

        protected override void OnContractActorsLinked(LinkLogicBlockContractActors message)
        {
            LinkCount++;
        }
    }

    /// <summary>The provider face's handler — development surface, so it declares the marker.</summary>
    [DevelopmentOnlyHandler]
    public class BindProbeProviderHandler : ServiceProviderHandlerBase
    {
        public BindProbeProviderHandler() : base(NullLogger.Instance)
        {
        }

        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (string.Empty, Array.Empty<string>());
        }

        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
        }

        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }

    /// <summary>The endpoint a property carries, implementing the sink half of the link contract.</summary>
    public sealed class BindSinkComponent : IBindSink
    {
        public List<BindLinkContract.Nudge> Nudges { get; } = [];

        public List<BindLinkContract.Poll> Polls { get; } = [];

        public void HandleCommand(BindLinkContract.Nudge command)
        {
            Nudges.Add(command);
        }

        public BindLinkContract.Reading HandleRequest(BindLinkContract.Poll request)
        {
            Polls.Add(request);
            return new BindLinkContract.Reading(request.Channel * 10);
        }
    }

    /// <summary>An endpoint implementing both halves, so one property yields two endpoints.</summary>
    public sealed class BindBothComponent : IBindSource, IBindSink
    {
        public void HandleCommand(BindLinkContract.Nudge command)
        {
        }

        public BindLinkContract.Reading HandleRequest(BindLinkContract.Poll request)
        {
            return new BindLinkContract.Reading(request.Channel);
        }

        public void HandleStateUpdate(InterfaceId functionId, BindLinkContract.Level response)
        {
        }

        public void HandleResponse(InterfaceId functionId, BindLinkContract.Reading response)
        {
        }
    }

    /// <summary>The source half on the block's own class — the class-implemented binding.</summary>
    public sealed class BindSourceBlock : LogicBlockBase, IBindSource
    {
        public List<BindLinkContract.Level> Levels { get; } = [];

        public List<BindLinkContract.Reading> Readings { get; } = [];

        public List<InterfaceId> Responders { get; } = [];

        public List<InterfaceId> LevelSenders { get; } = [];

        public BindSourceBlock() : base(NullLogger.Instance)
        {
        }

        public void HandleStateUpdate(InterfaceId functionId, BindLinkContract.Level response)
        {
            LevelSenders.Add(functionId);
            Levels.Add(response);
        }

        public void HandleResponse(InterfaceId functionId, BindLinkContract.Reading response)
        {
            Responders.Add(functionId);
            Readings.Add(response);
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>The sink half on a public property — the property-based binding.</summary>
    public sealed class BindSinkBlock : LogicBlockBase
    {
        public BindSinkComponent? Endpoint { get; }

        public BindSinkBlock(BindSinkComponent? endpoint = null) : base(NullLogger.Instance)
        {
            Endpoint = endpoint ?? new BindSinkComponent();
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A block binding one service-provider contract.</summary>
    public sealed class BindContractBlock : LogicBlockBase
    {
        public const string ContractIdentifier = "Probe";

        [ServiceProviderContractBinding(Identifier = ContractIdentifier, DefaultName = "The probe", Multiplicity = LinkMultiplicity.ExactlyOne, Tags = ["io", "probe"])]
        public IBindProbe? Probe { get; private set; }

        public BindContractBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A simulator-shaped block binding both an ordinary contract and a provider face.</summary>
    public sealed class BindProviderFaceBlock : LogicBlockBase
    {
        public const string FaceIdentifier = "Face";

        public const string ProbeIdentifier = "Probe";

        [ServiceProviderContractBinding(Identifier = FaceIdentifier)]
        public IBindProbeProvider? Face { get; private set; }

        [ServiceProviderContractBinding(Identifier = ProbeIdentifier)]
        public IBindProbe? Probe { get; private set; }

        public BindProviderFaceBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>The service provider a bind suite configures a block against.</summary>
    public static class BindHosts
    {
        public static readonly IServiceProvider Bare = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                              .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                              .BuildServiceProvider();
    }
}