using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test
{
    /// <summary>
    ///     What a host gets from the SDK's own registration. The clock rule is the whole of stepped mode —
    ///     a host registers a controllable clock first and the SDK leaves it alone — and the single vitals
    ///     core behind three faces is what keeps a counter from being split in two.
    /// </summary>
    [TestClass]
    public sealed class ServiceCollectionExtensionsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.1")]
        public void RegisterLoggerBlockCanTake()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddDaleSdk();

            // Assert
            using var provider = services.BuildServiceProvider();
            Assert.IsNotNull(provider.GetService<ILogger>(), "Every block's base constructor takes one, so it has to resolve.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.1")]
        public void RegisterRealClockWhenHostRegisteredNone()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddDaleSdk();

            // Assert
            using var provider = services.BuildServiceProvider();
            Assert.AreSame(TimeProvider.System, provider.GetRequiredService<TimeProvider>(), "A host that says nothing about time gets the real clock.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.1")]
        public void LeaveClockHostRegisteredFirstAlone()
        {
            // Arrange
            var controllable = new FakeTimeProvider();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(controllable);

            // Act
            services.AddDaleSdk();

            // Assert
            using var provider = services.BuildServiceProvider();
            Assert.AreSame(controllable, provider.GetRequiredService<TimeProvider>(), "Stepped mode rests entirely on this ordering: the host's clock wins.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-020.1")]
        public void ResolveOneVitalsCoreThroughEachOfItsFaces()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddDaleSdk();

            // Assert
            using var provider = services.BuildServiceProvider();
            var diagnostics = provider.GetRequiredService<IRuntimeDiagnostics>();
            Assert.AreSame<object>(diagnostics, provider.GetRequiredService<IActorMessageObserver>(), "A second instance would split every counter between two aggregates.");
            Assert.AreSame<object>(diagnostics, provider.GetRequiredService<IActorVitalsCollector>());
        }
    }
}