using System;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The service-qualified control overloads (RFC 0006 revision 5): duplicate member names across one
    ///     block's services collapse last-service-wins in the flat name map, so the qualified forms are the
    ///     only name-level way to reach the shadowed service.
    /// </summary>
    [TestClass]
    public class ServiceQualifiedControlShould
    {
        [TestMethod]
        public async Task DisambiguateDuplicateMemberNamesAcrossServices()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<DualPointBlock>("dual").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("dual", "PointA", "Limit", 3.5);
            await host.Control.SetPropertyAsync("dual", "PointB", "Limit", 7.5);

            Assert.AreEqual(3.5, host.Control.GetProperty("dual", "PointA", "Limit"));
            Assert.AreEqual(7.5, host.Control.GetProperty("dual", "PointB", "Limit"));

            // The flat two-argument form still resolves (the collapsed map) — it reaches ONE of the two
            // services; which one is an implementation detail the scenario format refuses to inherit.
            var collapsed = host.Control.GetProperty("dual", "Limit");
            Assert.IsTrue(Equals(collapsed, 3.5) || Equals(collapsed, 7.5), $"collapsed read returned {collapsed}");
        }

        [TestMethod]
        public async Task ReturnNullForUnknownServiceIdentifiers()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<DualPointBlock>("dual").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            Assert.IsNull(host.Control.GetProperty("dual", "PointC", "Limit"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.Control.SetPropertyAsync("dual", "PointC", "Limit", 1.0));
        }

        [TestMethod]
        public async Task PreferTheRootServiceForABareReadWhenANameCollidesWithNestedComponents()
        {
            // DF-47: a member name carried by BOTH the block's own (root) service and its nested components
            // (ActivePowerTotalKw on a buffer surface + on its charge points) collapsed last-service-wins in
            // the flat name map, so the bare read returned a component's 0 instead of the battery's value.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<RootNestedCollisionBlock>("collide").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("collide", "PointA", "SharedPower", 11.0);
            await host.Control.SetPropertyAsync("collide", "PointB", "SharedPower", 22.0);

            // The bare (two-argument) read of the shared name must resolve to the ROOT service (-40, emitted
            // from Ready), not collapse onto a nested component (11 / 22). The root value arrives via the
            // startup leading edge, so poll for it.
            var bare = await WaitForValueAsync(() => host.Control.GetProperty("collide", "SharedPower"), -40.0);
            Assert.AreEqual(-40.0, bare, "A bare read of a name shared by the root and nested components must resolve to the ROOT service, not a component.");

            // The service-qualified reads still reach each specific component (unchanged behaviour).
            Assert.AreEqual(11.0, host.Control.GetProperty("collide", "PointA", "SharedPower"));
            Assert.AreEqual(22.0, host.Control.GetProperty("collide", "PointB", "SharedPower"));
        }

        [TestMethod]
        public async Task ResolveADottedServiceMemberPathThroughTheBareRead()
        {
            // DF-47: the single-property state endpoint passes a "service.member" path whole to the two-arg
            // read. The bare-name flat map has no such key, so a nested "PointA.SharedPower" returned null;
            // it must instead route to the service-qualified resolver.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<RootNestedCollisionBlock>("collide").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("collide", "PointA", "SharedPower", 11.0);
            await host.Control.SetPropertyAsync("collide", "PointB", "SharedPower", 22.0);

            Assert.AreEqual(11.0, host.Control.GetProperty("collide", "PointA.SharedPower"));
            Assert.AreEqual(22.0, host.Control.GetProperty("collide", "PointB.SharedPower"));

            // An unknown service segment stays null rather than falling back to a false bare-name hit.
            Assert.IsNull(host.Control.GetProperty("collide", "PointZ.SharedPower"));
        }

        // The startup leading-edge publish of a read-only root property is asynchronous, so poll (bounded)
        // rather than assume read-after-StartAsync. Set values are read-after-write reliable and need no poll.
        private static async Task<object?> WaitForValueAsync(Func<object?> read, object? expected)
        {
            var value = read();
            for (var attempt = 0; attempt < 50 && !Equals(value, expected); attempt++)
            {
                await Task.Delay(100);
                value = read();
            }

            return value;
        }
    }
}