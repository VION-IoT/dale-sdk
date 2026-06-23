using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class EmissionPolicyForceMarkerShould
    {
        [TestMethod]
        public void BeResolvableFromAServiceProviderOnceRegistered()
        {
            // The marker is the contract between the TestKit (which registers it) and
            // LogicBlockBase (which reads m.ServiceProvider.GetService(typeof(EmissionPolicyForceMarker))
            // at InitializeLogicBlock to set _forcePolicyFromAttributes). Prove the type exists,
            // is a reference type usable as a DI key, and round-trips through a ServiceProvider.
            var services = new ServiceCollection();
            services.AddSingleton(new EmissionPolicyForceMarker());
            var provider = services.BuildServiceProvider();

            var resolved = provider.GetService(typeof(EmissionPolicyForceMarker));

            Assert.IsNotNull(resolved, "EmissionPolicyForceMarker must be resolvable once registered.");
            Assert.IsInstanceOfType<EmissionPolicyForceMarker>(resolved);
        }

        [TestMethod]
        public void BeAbsentFromAnEmptyServiceProvider()
        {
            // The default (policy-off) path must see no marker, so GetService returns null and
            // _forcePolicyFromAttributes stays false under the fake clock.
            var provider = new ServiceCollection().BuildServiceProvider();

            Assert.IsNull(provider.GetService(typeof(EmissionPolicyForceMarker)));
        }
    }
}