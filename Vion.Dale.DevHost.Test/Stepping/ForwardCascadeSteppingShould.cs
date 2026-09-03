using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The quiescence predicate on a fire-and-forget cascade, which is the shape that exposes it. The chain
    ///     is head <c>[Timer(1)]</c> to relay1 to relay2 to relay3 to sink, connected by one-way
    ///     <c>[Command]</c> contracts, so there is no reverse traffic to keep mailbox depth above zero while a
    ///     handler is between its dequeue and its next post. Every advance must deliver one token through all
    ///     four hops; a shortfall means the barrier declared quiescence mid-cascade.
    /// </summary>
    [TestClass]
    public class ForwardCascadeSteppingShould
    {
        private const int VirtualSeconds = 8;

        private const int Iterations = 15;

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.5")]
        public async Task DeliverEveryHopOfForwardOnlyCascadeBeforeTheNextAdvance()
        {
            // Arrange
            var arrivals = new int[Iterations];

            // Act — repeated because a barrier gap is a race: it shows on some runs and not others.
            for (var run = 0; run < Iterations; run++)
            {
                var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                    1,
                                                                    1,
                                                                    0,
                                                                    0,
                                                                    0,
                                                                    TimeSpan.Zero));

                await using var host = DevHostBuilder.Create()
                                                     .WithDi<ForwardChainDependencyInjection>()
                                                     .WithConfiguration(ForwardChainConfig.Build())
                                                     .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                     .Build();

                await host.StartAsync();
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(VirtualSeconds));
                arrivals[run] = (int)host.Control.GetProperty("sink", "Arrivals")!;
            }

            // Assert
            CollectionAssert.AreEqual(Enumerable.Repeat(VirtualSeconds, Iterations).ToArray(), arrivals);
        }
    }
}
