using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     DF-18 regression: a stepped host must be bit-reproducible on a FAN-IN network. Three source blocks
    ///     each fire a <c>[Timer(1)]</c> at the SAME virtual instant and send to one aggregator, which records
    ///     the order it received them. The aggregator's received-order is exactly the cross-actor processing
    ///     order within a quiescence round — which, under the default thread-pool dispatcher, is
    ///     thread-pool-dependent and varies run to run. The fix (deterministic in-round scheduling) must make
    ///     this order identical across fresh stepped hosts.
    /// </summary>
    [TestClass]
    public class FanInDeterminismShould
    {
        [TestMethod]
        public async Task ProduceTheSameReceiveOrderAcrossFreshSteppedHosts()
        {
            // Advance enough virtual seconds that the cross-actor order is exercised many times; a single
            // accidental agreement won't mask a real race. Compare the aggregator's received-order log across
            // independent fresh hosts — a deterministic stepper yields one identical log.
            const int seconds = 8;
            string? first = null;
            for (var run = 0; run < 8; run++)
            {
                await using var host = BuildFanInHost();
                await host.StartAsync();

                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(seconds));

                var log = host.Control.GetProperty("Aggregator", "ReceiveLog") as string ?? string.Empty;
                Assert.AreEqual(seconds * 3, log.Length, $"run {run}: expected {seconds * 3} receipts (3 sources × {seconds} ticks), got '{log}'");
                first ??= log;
                Assert.AreEqual(first, log, $"run {run}: fan-in receive order differs across fresh stepped hosts — not bit-reproducible (DF-18). '{first}' vs '{log}'");
            }
        }

        [TestMethod]
        public async Task ProduceTheSameReceiveOrderAcrossFreshSteppedHosts_ForAFanOutCascade()
        {
            // The EM's shape: ONE timer fires (the coordinator) and fans OUT to three workers within a single
            // cascade; each worker forwards to the aggregator. Without the serial dispatcher the three worker
            // handlers run concurrently on the thread pool, so the aggregator's receive order is racy even
            // though only one timer fired. The serial dispatcher drains the cascade in a single order.
            const int seconds = 8;
            string? first = null;
            for (var run = 0; run < 8; run++)
            {
                await using var host = BuildFanOutHost();
                await host.StartAsync();

                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(seconds));

                var log = host.Control.GetProperty("Aggregator", "ReceiveLog") as string ?? string.Empty;
                Assert.AreEqual(seconds * 3, log.Length, $"run {run}: expected {seconds * 3} receipts (1 coordinator tick → 3 workers × {seconds} ticks), got '{log}'");
                first ??= log;
                Assert.AreEqual(first, log, $"run {run}: fan-out cascade receive order differs across fresh stepped hosts — not bit-reproducible (DF-18). '{first}' vs '{log}'");
            }
        }

        private static IDevHost BuildFanInHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("fan-in")
                                                .AddLogicBlock<FanSourceA>("A", out var a)
                                                .AddLogicBlock<FanSourceB>("B", out var b)
                                                .AddLogicBlock<FanSourceC>("C", out var c)
                                                .AddLogicBlock<FanAggregatorBlock>("Aggregator", out var sink)
                                                .Connect(a, sink)
                                                .Connect(b, sink)
                                                .Connect(c, sink)
                                                .Build();
            return DevHostBuilder.Create().WithDi<FanInDependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static IDevHost BuildFanOutHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("fan-out")
                                                .AddLogicBlock<FanCoordinatorBlock>("Coordinator", out var coordinator)
                                                .AddLogicBlock<FanWorker1>("W1", out var w1)
                                                .AddLogicBlock<FanWorker2>("W2", out var w2)
                                                .AddLogicBlock<FanWorker3>("W3", out var w3)
                                                .AddLogicBlock<FanAggregatorBlock>("Aggregator", out var sink)
                                                .Connect(coordinator, w1)
                                                .Connect(coordinator, w2)
                                                .Connect(coordinator, w3)
                                                .Connect(w1, sink)
                                                .Connect(w2, sink)
                                                .Connect(w3, sink)
                                                .Build();
            return DevHostBuilder.Create().WithDi<FanOutDependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }
    }

    /// <summary>A fire-and-observe link: a source pings its aggregator(s); the aggregator acks. Mirrors the SignalLink shape.</summary>
    [LogicBlockContract(BetweenInterface = "IFanSource", AndInterface = "IFanSink", Direction = ContractDirection.Bidirectional)]
    public static class FanLink
    {
        [RequestResponse(From = "IFanSource", To = "IFanSink", ResponseType = typeof(Ack))]
        public readonly record struct Ping(int SourceId);

        public readonly record struct Ack(int SourceId);
    }

    /// <summary>Base for the three identical-cadence sources; each carries a distinct id so the aggregator can record order.</summary>
    public abstract class FanSourceBase : LogicBlockBase, IFanSource
    {
        protected abstract int SourceId { get; }

        protected FanSourceBase(ILogger logger) : base(logger)
        {
        }

        public void HandleResponse(InterfaceId functionId, FanLink.Ack response)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            foreach (var sink in this.GetLinkedFanSinks())
            {
                this.SendRequest(sink, new FanLink.Ping(SourceId));
            }
        }

        protected override void Ready()
        {
        }
    }

    [LogicBlock(Name = "Fan Source A")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanSourceA : FanSourceBase
    {
        protected override int SourceId
        {
            get => 1;
        }

        public FanSourceA(ILogger logger) : base(logger)
        {
        }
    }

    [LogicBlock(Name = "Fan Source B")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanSourceB : FanSourceBase
    {
        protected override int SourceId
        {
            get => 2;
        }

        public FanSourceB(ILogger logger) : base(logger)
        {
        }
    }

    [LogicBlock(Name = "Fan Source C")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanSourceC : FanSourceBase
    {
        protected override int SourceId
        {
            get => 3;
        }

        public FanSourceC(ILogger logger) : base(logger)
        {
        }
    }

    /// <summary>Records the order it receives pings as a digit string ("123123…") — the observable cross-actor order.</summary>
    [LogicBlock(Name = "Fan Aggregator")]
    [LogicBlockInterfaceBinding(typeof(IFanSink), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    public class FanAggregatorBlock : LogicBlockBase, IFanSink
    {
        [ServiceProperty(Title = "Receive order")]
        public string ReceiveLog { get; private set; } = string.Empty;

        public FanAggregatorBlock(ILogger logger) : base(logger)
        {
        }

        public FanLink.Ack HandleRequest(FanLink.Ping request)
        {
            ReceiveLog += request.SourceId.ToString();
            return new FanLink.Ack(request.SourceId);
        }

        protected override void Ready()
        {
        }
    }

    public class FanInDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<FanSourceA>();
            serviceCollection.AddTransient<FanSourceB>();
            serviceCollection.AddTransient<FanSourceC>();
            serviceCollection.AddTransient<FanAggregatorBlock>();
        }
    }

    /// <summary>
    ///     Fires one timer per virtual second and fans OUT to its linked workers — the cascade-fan-out half of the EM
    ///     shape.
    /// </summary>
    [LogicBlock(Name = "Fan Coordinator")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    public class FanCoordinatorBlock : LogicBlockBase, IFanSource
    {
        public FanCoordinatorBlock(ILogger logger) : base(logger)
        {
        }

        public void HandleResponse(InterfaceId functionId, FanLink.Ack response)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            foreach (var worker in this.GetLinkedFanSinks())
            {
                this.SendRequest(worker, new FanLink.Ping(0));
            }
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A relay: receives the coordinator's ping (as a sink) and forwards its own id to the aggregator (as a source).</summary>
    public abstract class FanWorkerBase : LogicBlockBase, IFanSource, IFanSink
    {
        protected abstract int WorkerId { get; }

        protected FanWorkerBase(ILogger logger) : base(logger)
        {
        }

        public FanLink.Ack HandleRequest(FanLink.Ping request)
        {
            foreach (var sink in this.GetLinkedFanSinks())
            {
                this.SendRequest(sink, new FanLink.Ping(WorkerId));
            }

            return new FanLink.Ack(request.SourceId);
        }

        public void HandleResponse(InterfaceId functionId, FanLink.Ack response)
        {
        }

        protected override void Ready()
        {
        }
    }

    [LogicBlock(Name = "Fan Worker 1")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IFanSink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanWorker1 : FanWorkerBase
    {
        protected override int WorkerId
        {
            get => 1;
        }

        public FanWorker1(ILogger logger) : base(logger)
        {
        }
    }

    [LogicBlock(Name = "Fan Worker 2")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IFanSink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanWorker2 : FanWorkerBase
    {
        protected override int WorkerId
        {
            get => 2;
        }

        public FanWorker2(ILogger logger) : base(logger)
        {
        }
    }

    [LogicBlock(Name = "Fan Worker 3")]
    [LogicBlockInterfaceBinding(typeof(IFanSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    [LogicBlockInterfaceBinding(typeof(IFanSink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class FanWorker3 : FanWorkerBase
    {
        protected override int WorkerId
        {
            get => 3;
        }

        public FanWorker3(ILogger logger) : base(logger)
        {
        }
    }

    public class FanOutDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<FanCoordinatorBlock>();
            serviceCollection.AddTransient<FanWorker1>();
            serviceCollection.AddTransient<FanWorker2>();
            serviceCollection.AddTransient<FanWorker3>();
            serviceCollection.AddTransient<FanAggregatorBlock>();
        }
    }
}