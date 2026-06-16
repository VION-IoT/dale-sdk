using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     A minimal 4-hop fire-and-forget relay chain used by <see cref="ForwardCascadeSteppingShould" />
    ///     to regression-test the exact quiescence barrier against the forward-only (no reverse traffic)
    ///     cascade shape. Each <c>[Timer(1)]</c> tick on the head fires a one-way <c>[Command]</c> that
    ///     travels head → relay1 → relay2 → relay3 → sink, where <c>Arrivals</c> increments on each
    ///     delivery. After N advances, <c>Arrivals</c> must equal N — any shortfall means the barrier
    ///     declared quiescence mid-cascade.
    /// </summary>

    // ── Contracts ──────────────────────────────────────────────────────────────────────────────────────
    [LogicBlockContract(BetweenInterface = "IChainHead", AndInterface = "IChainRelay1", Direction = ContractDirection.BetweenToAnd)]
    public static class ChainLink0
    {
        [Command(From = "IChainHead", To = "IChainRelay1")]
        public readonly record struct Token;
    }

    [LogicBlockContract(BetweenInterface = "IChainRelay1Out", AndInterface = "IChainRelay2", Direction = ContractDirection.BetweenToAnd)]
    public static class ChainLink1
    {
        [Command(From = "IChainRelay1Out", To = "IChainRelay2")]
        public readonly record struct Token;
    }

    [LogicBlockContract(BetweenInterface = "IChainRelay2Out", AndInterface = "IChainRelay3", Direction = ContractDirection.BetweenToAnd)]
    public static class ChainLink2
    {
        [Command(From = "IChainRelay2Out", To = "IChainRelay3")]
        public readonly record struct Token;
    }

    [LogicBlockContract(BetweenInterface = "IChainRelay3Out", AndInterface = "IChainSink", Direction = ContractDirection.BetweenToAnd)]
    public static class ChainLink3
    {
        [Command(From = "IChainRelay3Out", To = "IChainSink")]
        public readonly record struct Token;
    }

    // ── Head ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Head: fires one command into the chain on every [Timer(1)] tick.</summary>
    [LogicBlock(Name = "ChainHead")]
    [LogicBlockInterfaceBinding(typeof(IChainHead), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class ChainHeadBlock : LogicBlockBase, IChainHead
    {
        public ChainHeadBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            foreach (var relay in this.GetLinkedChainRelay1s())
            {
                this.SendCommand(relay, new ChainLink0.Token());
            }
        }

        protected override void Ready()
        {
        }
    }

    // ── Relays ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Relay 1: receives a hop-0 token and fire-and-forgets a hop-1 token onward.</summary>
    [LogicBlock(Name = "ChainRelay1")]
    [LogicBlockInterfaceBinding(typeof(IChainRelay1), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IChainRelay1Out), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class ChainRelay1Block : LogicBlockBase, IChainRelay1, IChainRelay1Out
    {
        public ChainRelay1Block(ILogger logger) : base(logger)
        {
        }

        public void HandleCommand(ChainLink0.Token command)
        {
            foreach (var next in this.GetLinkedChainRelay2s())
            {
                this.SendCommand(next, new ChainLink1.Token());
            }
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>Relay 2: receives a hop-1 token and fire-and-forgets a hop-2 token onward.</summary>
    [LogicBlock(Name = "ChainRelay2")]
    [LogicBlockInterfaceBinding(typeof(IChainRelay2), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IChainRelay2Out), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class ChainRelay2Block : LogicBlockBase, IChainRelay2, IChainRelay2Out
    {
        public ChainRelay2Block(ILogger logger) : base(logger)
        {
        }

        public void HandleCommand(ChainLink1.Token command)
        {
            foreach (var next in this.GetLinkedChainRelay3s())
            {
                this.SendCommand(next, new ChainLink2.Token());
            }
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>Relay 3: receives a hop-2 token and fire-and-forgets a hop-3 token onward.</summary>
    [LogicBlock(Name = "ChainRelay3")]
    [LogicBlockInterfaceBinding(typeof(IChainRelay3), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IChainRelay3Out), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class ChainRelay3Block : LogicBlockBase, IChainRelay3, IChainRelay3Out
    {
        public ChainRelay3Block(ILogger logger) : base(logger)
        {
        }

        public void HandleCommand(ChainLink2.Token command)
        {
            foreach (var next in this.GetLinkedChainSinks())
            {
                this.SendCommand(next, new ChainLink3.Token());
            }
        }

        protected override void Ready()
        {
        }
    }

    // ── Sink ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Terminal sink: counts deliveries on a service property checked by the test.</summary>
    [LogicBlock(Name = "ChainSink")]
    [LogicBlockInterfaceBinding(typeof(IChainSink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class ChainSinkBlock : LogicBlockBase, IChainSink
    {
        [ServiceProperty(Title = "Arrivals")]
        public int Arrivals { get; private set; }

        public ChainSinkBlock(ILogger logger) : base(logger)
        {
        }

        public void HandleCommand(ChainLink3.Token command)
        {
            Arrivals++;
        }

        protected override void Ready()
        {
        }
    }

    // ── DI + Configuration ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Registers all forward-chain blocks with the DI container.</summary>
    public class ForwardChainDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<ChainHeadBlock>();
            serviceCollection.AddTransient<ChainRelay1Block>();
            serviceCollection.AddTransient<ChainRelay2Block>();
            serviceCollection.AddTransient<ChainRelay3Block>();
            serviceCollection.AddTransient<ChainSinkBlock>();
        }
    }

    /// <summary>
    ///     Builds the 4-hop chain configuration: head → relay1 → relay2 → relay3 → sink.
    /// </summary>
    public static class ForwardChainConfig
    {
        public static DevConfiguration Build()
        {
            var builder = DevConfigurationBuilder.Create();

            builder.AddLogicBlock<ChainHeadBlock>("head", out var head);
            builder.AddLogicBlock<ChainRelay1Block>("relay1", out var relay1);
            builder.AddLogicBlock<ChainRelay2Block>("relay2", out var relay2);
            builder.AddLogicBlock<ChainRelay3Block>("relay3", out var relay3);
            builder.AddLogicBlock<ChainSinkBlock>("sink", out var sink);

            builder.Connect(head, relay1);
            builder.Connect(relay1, relay2);
            builder.Connect(relay2, relay3);
            builder.Connect(relay3, sink);

            return builder.Build();
        }
    }
}