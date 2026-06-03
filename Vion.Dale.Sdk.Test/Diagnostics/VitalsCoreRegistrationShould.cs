using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    [TestClass]
    public class VitalsCoreRegistrationShould
    {
        [TestMethod]
        public void ResolveTheVitalsCoreAsOneSharedSingletonAcrossItsInterfaces()
        {
            var services = new ServiceCollection();
            services.AddDaleSdk();
            using var provider = services.BuildServiceProvider();

            var diagnostics = provider.GetRequiredService<IRuntimeDiagnostics>();
            var observer = provider.GetRequiredService<IActorMessageObserver>();
            var collector = provider.GetRequiredService<IActorVitalsCollector>();

            Assert.AreSame<object>(diagnostics, observer);
            Assert.AreSame<object>(diagnostics, collector);
        }
    }
}
