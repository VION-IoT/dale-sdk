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
            await Assert.ThrowsExactlyAsync<System.InvalidOperationException>(() => host.Control.SetPropertyAsync("dual", "PointC", "Limit", 1.0));
        }
    }
}