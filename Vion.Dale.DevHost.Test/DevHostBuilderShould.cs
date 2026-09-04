using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     What building a development host guarantees before anything starts: the refusals, the defaults the
    ///     builder supplies so a caller need not, and the ordering the start depends on.
    /// </summary>
    [TestClass]
    public class DevHostBuilderShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.1")]
        public void RefuseToBuildWithoutConfiguration()
        {
            // Arrange
            var builder = DevHostBuilder.Create().WithDi<TestDependencyInjection>();

            // Act
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => builder.Build());

            // Assert — the only other outcome is a null reference from inside the first actor.
            StringAssert.Contains(refusal.Message, nameof(DevConfigurationBuilder));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.2")]
        public async Task SupplyLoggerFactoryWhenCallerRegisteredNone()
        {
            // Arrange — no ConfigureLogging call at all, which is how every example's Program.cs builds.
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();

            // Act
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();
            await host.StartAsync();

            // Assert — the host built and logged; without the default the build cannot even resolve its own logger.
            Assert.IsNotEmpty(host.Control.RecentLogs());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-002.1")]
        public async Task IntrospectBeforeStartingAnyHostedService()
        {
            // Arrange — a hosted service that reads the control surface the moment it starts. The web host is
            // one of these, and it serves /api/configuration from that instant: an unintrospected network
            // would answer with blocks that carry no services at all.
            var observed = new List<int>();
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(configuration)
                                                 .ConfigureServices(services => services.AddSingleton<IHostedService>(sp =>
                                                                                                                          new ServiceIdProbe(sp.GetRequiredService<IDevHostControl>(),
                                                                                                                              observed)))
                                                 .Build();

            // Act
            await host.StartAsync();

            // Assert
            Assert.HasCount(1, observed, "the probe hosted service must have been started exactly once");
            Assert.IsGreaterThan(0, observed[0], "introspection must have assigned the block's service ids before any hosted service started");
        }

        // Records how many service ids the wired block already had when this hosted service was started.
        private sealed class ServiceIdProbe : IHostedService
        {
            private readonly IDevHostControl _control;

            private readonly List<int> _observed;

            public ServiceIdProbe(IDevHostControl control, List<int> observed)
            {
                _control = control;
                _observed = observed;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _observed.Add(_control.ListLogicBlocks()[0].ServiceIds.Count);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
