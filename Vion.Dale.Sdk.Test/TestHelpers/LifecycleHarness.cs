using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     Drives a logic block through the message sequence the runtime sends, against a context that records
    ///     what the block answered, published and scheduled. The block's own guarantees are observable from
    ///     those three lists alone, so nothing here reaches into the block by reflection
    ///     (<c>testing-conventions.md</c> section 7).
    ///     <para>
    ///         The shape is the one <c>LogicBlockShutdownGuardShould</c> carried privately, promoted here so
    ///         every suite of the lifecycle area drives a block the same way. Claims about <em>delivery</em> —
    ///         a delayed send arriving, an acknowledgement reaching a waiter, a scope being disposed — belong
    ///         to <c>Vion.Dale.ProtoActor.Test</c> instead, over a real actor system.
    ///     </para>
    /// </summary>
    public sealed class LifecycleHarness
    {
        /// <summary>The service provider a block is configured with — loggers only, the bare-host shape.</summary>
        public static readonly IServiceProvider BareHost = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                  .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                  .BuildServiceProvider();

        /// <summary>
        ///     The same host carrying a controllable clock, which is what turns the emission policy off
        ///     (<c>AC-EMIT-001.*</c>). A value change is then published as it happens or not at all, with no
        ///     throttle to defer it and no stop-time drain to carry it — which is what makes a claim about the
        ///     started flag itself observable.
        /// </summary>
        public static readonly IServiceProvider SteppedHost = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                     .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                     .AddSingleton<TimeProvider>(new FakeTimeProvider())
                                                                                     .BuildServiceProvider();

        public RecordingActorContext Context { get; } = new();

        /// <summary>What the block answered its requester, oldest first.</summary>
        public IReadOnlyList<object> Responses
        {
            get => Context.Responses;
        }

        /// <summary>What the block published to a handler, oldest first.</summary>
        public IReadOnlyList<object> Published
        {
            get => Context.Sent.Select(sent => sent.Message).ToList();
        }

        /// <summary>What the block scheduled for itself, with the delay each was armed at.</summary>
        public IReadOnlyList<(object Message, TimeSpan Delay)> Scheduled
        {
            get => Context.Scheduled;
        }

        /// <summary>
        ///     Everything the block sent, scheduled or answered, oldest first, in one sequence — which is what
        ///     an ordering claim needs: the three lists above each hold their own kind, so a test reading them
        ///     separately can see that every step happened and not that they happened in the stated order.
        /// </summary>
        public IReadOnlyList<object> Log
        {
            get => Context.Log;
        }

        /// <summary>A configuration message carrying <paramref name="serviceIdentifiers" /> as the block's services.</summary>
        public static InitializeLogicBlock Configuration(string name = "Block",
                                                         string id = "block-1",
                                                         IEnumerable<string>? serviceIdentifiers = null,
                                                         IServiceProvider? serviceProvider = null)
        {
            var lookup = (serviceIdentifiers ?? []).ToDictionary(identifier => identifier, identifier => new ServiceIdentifier(identifier));
            return new InitializeLogicBlock(id, name, lookup, new Dictionary<string, LogicBlockContractId>(), serviceProvider ?? BareHost);
        }

        /// <summary>The runtime-actor link, pointing every handler reference at a recording stand-in.</summary>
        public static LinkRuntimeActors RuntimeActors()
        {
            return new LinkRuntimeActors
                   {
                       ServicePropertyHandlerActor = new NamedReference("ServicePropertyHandler"),
                       ServiceMeasuringPointHandlerActor = new NamedReference("ServiceMeasuringPointHandler"),
                       PersistenceManagerActor = new NamedReference("PersistenceManager"),
                   };
        }

        /// <summary>Hands one message to the block, exactly as its actor would.</summary>
        public void Send(LogicBlockBase logicBlock, object message)
        {
            logicBlock.HandleMessageAsync(message, Context).GetAwaiter().GetResult();
        }

        /// <summary>Links the runtime actors, then configures the block — the order both in-repo hosts use.</summary>
        public void Configure(LogicBlockBase logicBlock, IEnumerable<string>? serviceIdentifiers = null, IServiceProvider? serviceProvider = null)
        {
            Send(logicBlock, RuntimeActors());
            Send(logicBlock, Configuration(serviceIdentifiers: serviceIdentifiers, serviceProvider: serviceProvider));
        }

        /// <summary>Configures the block and starts it.</summary>
        public void ConfigureAndStart(LogicBlockBase logicBlock, IEnumerable<string>? serviceIdentifiers = null, IServiceProvider? serviceProvider = null)
        {
            Configure(logicBlock, serviceIdentifiers, serviceProvider);
            Send(logicBlock, new StartLogicBlockRequest());
        }

        /// <summary>Hands back every message the block scheduled for itself whose type is named <paramref name="typeName" />.</summary>
        public IReadOnlyList<object> ScheduledOfKind(string typeName)
        {
            return Context.Scheduled.Where(entry => entry.Message.GetType().Name == typeName).Select(entry => entry.Message).ToList();
        }

        /// <summary>
        ///     Where in <see cref="Log" /> the first message named <paramref name="typeName" /> sits, or -1 when
        ///     the block produced none — so a test states an order as one comparison of two positions.
        /// </summary>
        public int FirstIndexOf(string typeName)
        {
            return Context.Log.FindIndex(message => message.GetType().Name == typeName);
        }

        /// <summary>An actor reference that carries the name it was looked up under, so a test can tell the handlers apart.</summary>
        public sealed class NamedReference : IActorReference
        {
            public string Name { get; }

            public NamedReference(string name)
            {
                Name = name;
            }
        }

        /// <summary>
        ///     The minimal actor context a block needs: it records the answers, the publications and the
        ///     self-sends, and mints a named reference for any name looked up — the same unconditional mint
        ///     the real context makes.
        /// </summary>
        public sealed class RecordingActorContext : IActorContext
        {
            public List<object> Responses { get; } = [];

            public List<(string Target, object Message)> Sent { get; } = [];

            public List<(object Message, TimeSpan Delay)> Scheduled { get; } = [];

            /// <summary>The three lists above interleaved, in the order the block produced them.</summary>
            public List<object> Log { get; } = [];

            public IReadOnlyDictionary<string, string>? Headers
            {
                get => null;
            }

            public void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null)
            {
                Sent.Add(((target as NamedReference)?.Name ?? "unnamed", message));
                Log.Add(message);
            }

            public void SendToSelf(object message)
            {
                Scheduled.Add((message, TimeSpan.Zero));
                Log.Add(message);
            }

            public void SendToSelfAfter(object message, TimeSpan delay)
            {
                Scheduled.Add((message, delay));
                Log.Add(message);
            }

            public void RespondToSender(object message)
            {
                Responses.Add(message);
                Log.Add(message);
            }

            public IActorReference LookupByName(string name)
            {
                return new NamedReference(name);
            }
        }
    }

    /// <summary>A block with nothing on it — the smallest thing the sequence can be driven over.</summary>
    public sealed class BareBlock : LogicBlockBase
    {
        public int ReadyCount { get; private set; }

        public int StartingCount { get; private set; }

        public int StoppingCount { get; private set; }

        public BareBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
            ReadyCount++;
        }

        protected override void Starting()
        {
            StartingCount++;
        }

        protected override void Stopping()
        {
            StoppingCount++;
        }
    }

    /// <summary>A block carrying one service, so persistence and the announcement have something to describe.</summary>
    public sealed class ServiceBearingBlock : LogicBlockBase
    {
        public Meter Service { get; } = new();

        public int StoppingCount { get; private set; }

        public ServiceBearingBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Stopping()
        {
            StoppingCount++;
        }
    }

    /// <summary>The service <see cref="ServiceBearingBlock" /> carries.</summary>
    public sealed class Meter
    {
        [ServiceProperty]
        public double Power { get; set; }

        [ServiceProperty]
        public int Count { get; set; }

        [ServiceProperty]
        public double? Optional { get; set; }

        /// <summary>A writable service property whose author opted it out of persistence.</summary>
        [ServiceProperty]
        [Persistent(Exclude = true)]
        public double Excluded { get; set; }
    }

    /// <summary>A block whose declarative binding throws, leaving the instance configured and unusable.</summary>
    public sealed class FailingConfigurationBlock : LogicBlockBase
    {
        /// <summary>The binder invokes this getter to reach the service inside, so the configuration fails there.</summary>
        public Meter Service
        {
            get => throw new InvalidOperationException("the declarative binder could not read this service");
        }

        public int StartingCount { get; private set; }

        public int StoppingCount { get; private set; }

        public FailingConfigurationBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
            StartingCount++;
        }

        protected override void Stopping()
        {
            StoppingCount++;
        }
    }

    /// <summary>A block whose hooks throw, one hook per instance.</summary>
    public sealed class ThrowingHookBlock : LogicBlockBase
    {
        public enum Hook
        {
            Ready,

            Starting,

            Stopping,
        }

        public Hook Throwing { get; }

        public ThrowingHookBlock(Hook hook) : base(NullLogger.Instance)
        {
            Throwing = hook;
        }

        protected override void Ready()
        {
            if (Throwing == Hook.Ready)
            {
                throw new InvalidOperationException("the ready hook refused");
            }
        }

        protected override void Starting()
        {
            if (Throwing == Hook.Starting)
            {
                throw new InvalidOperationException("the start hook refused");
            }
        }

        protected override void Stopping()
        {
            if (Throwing == Hook.Stopping)
            {
                throw new InvalidOperationException("the stop hook refused");
            }
        }
    }

    /// <summary>The contract the lifecycle fixtures link across, so a linked-interface map has somewhere to land.</summary>
    [LogicBlockContract(BetweenInterface = "ILifecyclePeer", AndInterface = "ILifecycleHub")]
    public static class LifecycleLinkContract
    {
        [StateUpdate(From = "ILifecycleHub", To = "ILifecyclePeer")]
        public readonly record struct Ping(int Count);
    }

    /// <summary>A block binding one interface endpoint, so a linked-interface map has an endpoint to reach.</summary>
    [LogicBlockInterfaceBinding(typeof(ILifecycleHub), Identifier = InterfaceIdentifier)]
    public sealed class InterfaceBearingBlock : LogicBlockBase, ILifecycleHub
    {
        public const string InterfaceIdentifier = "Hub";

        public InterfaceBearingBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>The endpoint a property carries, so one block can bind two interface endpoints at once.</summary>
    public sealed class HubEndpoint : ILifecycleHub
    {
    }

    /// <summary>
    ///     A block binding two interface endpoints — one on the class, one on a property — so a linked-interface
    ///     map can name one of them and a second map the other.
    /// </summary>
    [LogicBlockInterfaceBinding(typeof(ILifecycleHub), Identifier = ClassEndpointIdentifier)]
    public sealed class TwoEndpointBlock : LogicBlockBase, ILifecycleHub
    {
        public const string ClassEndpointIdentifier = "Hub";

        public const string PropertyEndpointIdentifier = "Peer";

        // DALE045 warns that an endpoint carrying no service member emits no relation half. Deliberate here:
        // the fixture is about which link mappings survive, not about what the endpoint publishes.
#pragma warning disable DALE045
        [LogicBlockInterfaceBinding(typeof(ILifecycleHub), Identifier = PropertyEndpointIdentifier)]
        public HubEndpoint Endpoint { get; } = new();
#pragma warning restore DALE045

        public TwoEndpointBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A persisted entry the storage layer handed back in its serialised form.</summary>
    public static class PersistedEntry
    {
        public static PersistentDataEntry Of(string key, string typeFullName, object value)
        {
            return new PersistentDataEntry(key, typeFullName, value);
        }
    }
}