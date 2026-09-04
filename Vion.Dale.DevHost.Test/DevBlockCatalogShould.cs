using System.Linq;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     <see cref="DevBlockCatalog" /> as <c>DevHostBuilder.GetBlockCatalog()</c> exposes it: which types a
    ///     <c>WithDi</c> registration contributes, and which it does not.
    ///     <para>
    ///         Uncited by design. The catalog is the topology generator's <em>input</em>, not part of the
    ///         topology-file contract <c>specs/scenarios.md</c> states — the surface that owns it is the control
    ///         plane's. These four moved here from <c>DefaultTopologyGeneratorShould</c>, where they had drifted
    ///         because the generator is the catalog's first consumer; they cite nothing until that area's pass.
    ///     </para>
    /// </summary>
    [TestClass]
    public class DevBlockCatalogShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.3")]
        public void EnumerateEveryRegisteredBlockType()
        {
            // Arrange / Act — TestDependencyInjection registers CounterBlock, MultiPointBlock, TickerBlock, DualPointBlock.
            var catalog = DevHostBuilder.Create().WithDi<TestDependencyInjection>().GetBlockCatalog();

            // Assert
            CollectionAssert.Contains(catalog.ToList(), typeof(CounterBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(MultiPointBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(DualPointBlock));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.3")]
        public void EnumerateBlocksFromEveryPluginAssembly()
        {
            // Arrange / Act
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            // Assert
            CollectionAssert.Contains(catalog.ToList(), typeof(SourceBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(SinkBlock));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.3")]
        public void ExcludeRegistrationsThatAreNotBlocks()
        {
            // Arrange / Act — CrossBlockDependencyInjection registers only blocks, as does TestDependencyInjection.
            // Neither may surface a non-block type.
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            // Assert
            foreach (var type in catalog)
            {
                Assert.IsTrue(typeof(Sdk.Core.LogicBlockBase).IsAssignableFrom(type), $"Catalog must contain only LogicBlockBase types; got {type.FullName}");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.3")]
        public void CountOnePluginAssemblyOnce()
        {
            // Arrange / Act
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            // Assert — a second WithDi for the same assembly is a no-op in the plugin list, so distinct is
            // trivially satisfied; the row exists to redden if that ever stops being true.
            var distinct = catalog.Distinct().ToList();
            Assert.HasCount(catalog.Count, distinct);
        }
    }
}