using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;
using IActorSystem = Vion.Dale.Sdk.Abstractions.IActorSystem;

namespace Vion.Dale.ProtoActor.Test.Extensions
{
    /// <summary>
    ///     What a host gets from the actor system's own registration, and what it does not. The clock is the
    ///     one that matters: an actor system composed without the SDK's registrations falls back to the real
    ///     clock, so every timeout and every measurement runs on wall time and nothing can be stepped.
    /// </summary>
    [TestClass]
    public sealed class ServiceCollectionExtensionsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.2")]
        public async Task RegisterActorSystemAndItsProtoHost()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddProtoActorSystem();

            // Assert
            await using var provider = services.BuildServiceProvider();
            Assert.IsNotNull(provider.GetService<IActorSystem>(), "The system a host spawns blocks onto resolves from this registration alone.");
            Assert.IsNotNull(provider.GetService<Proto.ActorSystem>(), "Along with the actor host underneath it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.2")]
        public void RegisterNoClockOfItsOwn()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddProtoActorSystem();

            // Assert
            using var provider = services.BuildServiceProvider();
            Assert.IsNull(provider.GetService<TimeProvider>(),
                          "The pipeline takes its clock from whatever the host registered; composing this alone leaves it on wall time and unable to be stepped.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.2")]
        public void RegisterNoObservationSeamOfItsOwn()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddProtoActorSystem();

            // Assert
            using var provider = services.BuildServiceProvider();
            Assert.IsNull(provider.GetService<IActorMessageObserver>(),
                          "The tap, the pause and the stepping are opt-in by registration, which is why production is unaffected by them.");
            Assert.IsNull(provider.GetService<IActorActivityMonitor>());
            Assert.IsNull(provider.GetService<IDelayedSendGate>());
            Assert.IsNull(provider.GetService<IVirtualSchedule>());
        }
    }
}