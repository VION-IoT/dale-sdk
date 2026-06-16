using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Regression test for the exact quiescence barrier on a fire-and-forget (forward-only) cascade
    ///     under next-event stepping. The chain is head [Timer(1)] → relay1 → relay2 → relay3 → sink
    ///     [Arrivals++], connected by one-way <c>[Command]</c> contracts (no reverse traffic).
    ///     <c>AdvanceAsync(Ns)</c> advances to each head tick (t=1..N) and waits for the exact predicate
    ///     <c>Σ MailboxDepth == 0 AND InFlight == 0</c> before the next advance, so every tick delivers
    ///     exactly one token through all four hops to the sink. After advancing N virtual seconds
    ///     <c>Arrivals</c> must equal N — any shortfall means the barrier declared quiescence mid-cascade.
    /// </summary>
    [TestClass]
    public class ForwardCascadeSteppingShould
    {
        private const int VirtualSeconds = 8;

        private const int Iterations = 15;

        [TestMethod]
        public async Task StepDeterministically_AcrossMany4HopRuns()
        {
            for (var run = 0; run < Iterations; run++)
            {
                var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                    1,
                                                                    1,
                                                                    0,
                                                                    0,
                                                                    0,
                                                                    TimeSpan.Zero));
                var config = ForwardChainConfig.Build();

                await using var host = DevHostBuilder.Create()
                                                     .WithDi<ForwardChainDependencyInjection>()
                                                     .WithConfiguration(config)
                                                     .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                     .Build();

                await host.StartAsync();

                // Advance N virtual seconds: next-event stepping fires the head [Timer(1)] at t=1..N,
                // each waiting for the 4-hop fire-and-forget cascade to settle before the next advance.
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(VirtualSeconds));

                var arrivals = (int)host.Control.GetProperty("sink", "Arrivals")!;
                Assert.AreEqual(VirtualSeconds,
                                arrivals,
                                $"run {run}: advancing {VirtualSeconds} virtual seconds must drive exactly {VirtualSeconds} arrivals " +
                                "through the 4-hop forward-only cascade. A mismatch means the exact quiescence " + "barrier declared quiescence mid-cascade (short-read).");
            }
        }
    }
}