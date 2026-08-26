using System;
using System.Linq;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     VION-77: the service-config ids were minted per boot with <c>Guid.NewGuid()</c>, so two runs of
    ///     the same wired config produced different <c>dale dev --export-config</c> output and the export
    ///     could not be cached, committed or diffed. They are now derived from the pair that already
    ///     identifies the service — the block id and the service identifier.
    /// </summary>
    [TestClass]
    public class DeterministicServiceIdsShould
    {
        [TestMethod]
        public async Task DeriveEachIdFromTheBlockIdAndTheServiceIdentifier()
        {
            // The assertion is the exact derived string, not "the ids in this process agree" — the weaker
            // form passes with random GUIDs too, so it proves nothing about the export.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<RootNestedCollisionBlock>("collide", "lb_collide").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            var block = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Id == "lb_collide");

            // The block's own (root) service is identified by the class name; each nested interface-bound
            // component by the property holding it — so all three ids of one block stay distinct.
            CollectionAssert.AreEquivalent(new[]
                                           {
                                               "lbsvc_lb_collide.RootNestedCollisionBlock",
                                               "lbsvc_lb_collide.PointA",
                                               "lbsvc_lb_collide.PointB",
                                           },
                                           block.Services.Select(s => s.Id).ToArray());
        }

        [TestMethod]
        public async Task KeepEveryServiceIdUniqueAcrossBlocksAndTheServiceProviderSpace()
        {
            // GUIDs made uniqueness free. Derived ids do not, and the ids still key the SPA's value map and
            // DevHostControl's write path — so a collision would silently cross two services' state. The
            // service-provider identifiers minted by DevConfigurationBuilder (svc_{blockId}, svc_shared_{n})
            // reach committed topology files, which is why the block-service space carries its own prefix.
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<RootNestedCollisionBlock>("first")
                                                .AddLogicBlock<RootNestedCollisionBlock>("second")
                                                .AddLogicBlock<DualPointBlock>("dual")
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            var configuration = host.Control.GetConfiguration();
            var ids = configuration.LogicBlocks
                                   .SelectMany(b => b.Services.Select(s => s.Id))
                                   .Concat(configuration.ServiceProviders.SelectMany(sp => sp.Services.Select(s => s.Identifier)))
                                   .ToList();

            CollectionAssert.AreEquivalent(ids.Distinct().ToList(), ids, $"duplicate service id: {string.Join(", ", ids)}");
        }

        [TestMethod]
        public void RefuseToBuildATopologyWithDuplicateBlockIds()
        {
            // The derived id is only unique while block ids are. An explicit id of the auto-assigned shape
            // collides with one, because the counter only advances for auto-assigned ids — and the collision
            // would route two blocks' reads and writes onto one service id, where GUIDs kept them apart.
            var builder = DevConfigurationBuilder.Create()
                                                 .AddLogicBlock<RootNestedCollisionBlock>("explicit", "lb_1")
                                                 .AddLogicBlock<RootNestedCollisionBlock>("auto0")
                                                 .AddLogicBlock<DualPointBlock>("auto1");

            var error = Assert.ThrowsExactly<InvalidOperationException>(() => builder.Build());

            StringAssert.Contains(error.Message, "lb_1");
            StringAssert.Contains(error.Message, "explicit");
            StringAssert.Contains(error.Message, "auto1");
        }

        [TestMethod]
        public async Task MintTheSameIdsForASecondHostOnTheSameConfiguration()
        {
            // The export is taken from a fresh process each time, and a recycle rebuilds the host in place —
            // both must land on the same ids as the run before.
            var first = await ServiceIdsOfAFreshHostAsync();
            var second = await ServiceIdsOfAFreshHostAsync();

            CollectionAssert.AreEqual(first, second);
        }

        private static async Task<string[]> ServiceIdsOfAFreshHostAsync()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<RootNestedCollisionBlock>("collide").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            return host.Control.GetConfiguration().LogicBlocks.SelectMany(b => b.Services.Select(s => s.Id)).ToArray();
        }
    }
}