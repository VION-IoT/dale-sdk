using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Regression test for the exact quiescence barrier on a fire-and-forget (forward-only) cascade.
    ///     The chain is head [Timer(1)] → relay1 → relay2 → relay3 → sink [Arrivals++], connected by
    ///     one-way <c>[Command]</c> contracts (no reverse traffic). After each <c>Advance(1s)</c> the
    ///     barrier waits for the exact predicate <c>Σ MailboxDepth == 0 AND InFlight == 0</c> before
    ///     returning, so every advance delivers exactly one token to the sink. After N advances
    ///     <c>Arrivals</c> must equal N — any shortfall means the barrier declared quiescence mid-cascade.
    /// </summary>
    [TestClass]
    public class ForwardCascadeSteppingShould
    {
        private const int Cycles = 8;

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

                // Each cycle advances the fake clock 1 s (one head tick) and waits for the 4-hop
                // fire-and-forget cascade to settle before the next advance.
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), Cycles);

                var arrivals = (int)host.Control.GetProperty("sink", "Arrivals")!;
                Assert.AreEqual(Cycles,
                                arrivals,
                                $"run {run}: {Cycles} deterministic cycles must drive exactly {Cycles} arrivals " +
                                "through the 4-hop forward-only cascade. A mismatch means the exact quiescence " + "barrier declared quiescence mid-cascade (short-read).");
            }
        }
    }
}