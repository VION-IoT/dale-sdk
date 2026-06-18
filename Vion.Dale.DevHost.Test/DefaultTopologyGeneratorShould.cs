using System;
using System.IO;
using System.Linq;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     <see cref="DefaultTopologyGenerator" />: auto-generation of a default topology from a DI block
    ///     catalog, and the round-trip through <see cref="DevTopologyLoader" />.
    /// </summary>
    [TestClass]
    public class DefaultTopologyGeneratorShould
    {
        // ──────────────────────────────────────────────────────────────────────────────
        // Generate
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Generate_ProducesOneInstancePerCatalogType()
        {
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(SourceBlock), typeof(SinkBlock) });

            Assert.HasCount(2, config.LogicBlocks);

            var names = config.LogicBlocks.Select(lb => lb.Name).ToList();
            CollectionAssert.Contains(names, nameof(SourceBlock));
            CollectionAssert.Contains(names, nameof(SinkBlock));
        }

        [TestMethod]
        public void Generate_AutoConnectsMatchingInterfacePairs()
        {
            // SourceBlock implements ISource, SinkBlock implements ISink — they share the PollLink contract.
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(SourceBlock), typeof(SinkBlock) });

            Assert.IsNotEmpty(config.InterfaceMappings, "SourceBlock/SinkBlock should produce at least one interface mapping");

            var mapping = config.InterfaceMappings[0];
            Assert.AreEqual("ISource", mapping.SourceInterfaceIdentifier);
            Assert.AreEqual("ISink", mapping.TargetInterfaceIdentifier);
        }

        [TestMethod]
        public void Generate_LeavesAmbiguousInterfacesUnwired()
        {
            // Two sources both match the one sink — AutoConnect over this uncurated catalog would wire a
            // fighting network (two commanders on one device). The conflict guard leaves the ambiguous
            // interface unwired (RFC 0008 §6.3): no mapping survives, because the sink's ISink matches both.
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(SourceBlock), typeof(SecondSourceBlock), typeof(SinkBlock) });

            Assert.HasCount(3, config.LogicBlocks);
            Assert.IsEmpty(config.InterfaceMappings,
                           "ISink matches two sources — the ambiguous wiring must be skipped, not wired to a fighting network. " + "Mappings: " +
                           string.Join(", ", config.InterfaceMappings.Select(m => $"{m.SourceLogicBlockName}->{m.TargetLogicBlockName}")));
        }

        [TestMethod]
        public void Generate_AutoWiresAFanInAggregator()
        {
            // Three sources each implement IFanSource; the aggregator binds IFanSink with ZeroOrMore — the
            // legitimate many-sources → one-aggregator fan-in (the RefGridMeter shape). Unlike command
            // contention (a single-writer interface matched by many, left unwired by the test above), the
            // many-multiplicity binding means all three wires must SURVIVE AutoConnect (DF-19).
            var config = DefaultTopologyGenerator.Generate(new[]
                                                           {
                                                               typeof(Stepping.FanSourceA),
                                                               typeof(Stepping.FanSourceB),
                                                               typeof(Stepping.FanSourceC),
                                                               typeof(Stepping.FanAggregatorBlock),
                                                           });

            Assert.HasCount(3,
                            config.InterfaceMappings,
                            "All three IFanSource → IFanSink (ZeroOrMore aggregator) wires must survive — a fan-in is not contention. Mappings: " +
                            string.Join(", ", config.InterfaceMappings.Select(m => $"{m.SourceLogicBlockName}->{m.TargetLogicBlockName}")));
            Assert.IsTrue(config.InterfaceMappings.All(m => m.TargetInterfaceIdentifier == "IFanSink"), "every fan-in wire must target the aggregator's IFanSink");
        }

        [TestMethod]
        public void Generate_SetsTopologyId()
        {
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(CounterBlock) }, "my-catalog");

            Assert.AreEqual("my-catalog", config.TopologyName);
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // ToTopologyFile + round-trip via DevTopologyLoader.Build
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ToTopologyFile_SetsSchemaRefAndId()
        {
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(CounterBlock) });
            var file = DefaultTopologyGenerator.ToTopologyFile(config);

            Assert.AreEqual(DevTopologyFile.SchemaRef, file.Schema);
            Assert.AreEqual("default", file.Id);
        }

        [TestMethod]
        public void ToTopologyFile_EmitsCorrectTypeFullNames()
        {
            var config = DefaultTopologyGenerator.Generate(new[] { typeof(SourceBlock), typeof(SinkBlock) });
            var file = DefaultTopologyGenerator.ToTopologyFile(config);

            var typeNames = file.LogicBlockInstances!.Select(i => i.TypeFullName).ToList();
            CollectionAssert.Contains(typeNames, typeof(SourceBlock).FullName);
            CollectionAssert.Contains(typeNames, typeof(SinkBlock).FullName);
        }

        [TestMethod]
        public void RoundTrip_LoaderReproducesEquivalentWiring()
        {
            // Generate → project → DevTopologyLoader.Build should give the same blocks and interface mappings.
            var blockTypes = new[] { typeof(SourceBlock), typeof(SinkBlock) };
            var config = DefaultTopologyGenerator.Generate(blockTypes);
            var file = DefaultTopologyGenerator.ToTopologyFile(config);

            // DevTopologyLoader.Build loads from the projected file — no file I/O needed.
            var reloaded = DevTopologyLoader.Build(file);

            CollectionAssert.AreEquivalent(config.LogicBlocks.Select(lb => lb.Name).ToList(), reloaded.LogicBlocks.Select(lb => lb.Name).ToList());

            Assert.HasCount(config.InterfaceMappings.Count, reloaded.InterfaceMappings);

            if (config.InterfaceMappings.Count > 0)
            {
                Assert.AreEqual(config.InterfaceMappings[0].SourceInterfaceIdentifier, reloaded.InterfaceMappings[0].SourceInterfaceIdentifier);

                Assert.AreEqual(config.InterfaceMappings[0].TargetInterfaceIdentifier, reloaded.InterfaceMappings[0].TargetInterfaceIdentifier);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // WriteDefault
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void WriteDefault_WritesAValidTopologyFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-default-topo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = DefaultTopologyGenerator.WriteDefault(new[] { typeof(SourceBlock), typeof(SinkBlock) }, dir);

                Assert.IsTrue(File.Exists(path), "topology file should be written to disk");
                Assert.IsTrue(path.EndsWith("default" + DevTopologyFile.FileSuffix, StringComparison.OrdinalIgnoreCase));

                // Must parse cleanly via the strict parser.
                var parsed = DevTopologyFile.Load(path);
                Assert.AreEqual("default", parsed.Id);
                Assert.IsNotNull(parsed.LogicBlockInstances);
                Assert.HasCount(2, parsed.LogicBlockInstances!);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [TestMethod]
        public void WriteDefault_DoesNotOverwriteExistingFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-default-topo-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);

                // Write a sentinel file manually.
                var existingPath = Path.Combine(dir, "default" + DevTopologyFile.FileSuffix);
                var sentinelContent = """
                                      {
                                        "id": "default",
                                        "logicBlockInstances": [ { "typeFullName": "Vion.Dale.DevHost.Test.CounterBlock", "name": "CounterBlock" } ]
                                      }
                                      """;
                File.WriteAllText(existingPath, sentinelContent);

                // WriteDefault should return the same path without touching the file.
                var returned = DefaultTopologyGenerator.WriteDefault(new[] { typeof(SourceBlock), typeof(SinkBlock) }, dir);

                Assert.AreEqual(existingPath, returned, StringComparer.OrdinalIgnoreCase);
                Assert.AreEqual(sentinelContent, File.ReadAllText(existingPath), "existing file must not be overwritten");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // Catalog enumeration from DevHostBuilder.GetBlockCatalog()
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void DevHostBuilder_GetBlockCatalog_ReturnsRegisteredBlockTypes()
        {
            // TestDependencyInjection registers CounterBlock, MultiPointBlock, TickerBlock, DualPointBlock.
            var catalog = DevHostBuilder.Create().WithDi<TestDependencyInjection>().GetBlockCatalog();

            CollectionAssert.Contains(catalog.ToList(), typeof(CounterBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(MultiPointBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(DualPointBlock));
        }

        [TestMethod]
        public void DevHostBuilder_GetBlockCatalog_ReturnsSourceAndSinkFromCrossBlockDi()
        {
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            CollectionAssert.Contains(catalog.ToList(), typeof(SourceBlock));
            CollectionAssert.Contains(catalog.ToList(), typeof(SinkBlock));
        }

        [TestMethod]
        public void DevHostBuilder_GetBlockCatalog_ExcludesNonBlockServices()
        {
            // CrossBlockDependencyInjection only registers SourceBlock + SinkBlock — no non-block services.
            // TestDependencyInjection similarly registers only blocks. Neither should surface non-block types.
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            foreach (var type in catalog)
            {
                Assert.IsTrue(typeof(Sdk.Core.LogicBlockBase).IsAssignableFrom(type), $"Catalog must contain only LogicBlockBase types; got {type.FullName}");
            }
        }

        [TestMethod]
        public void DevHostBuilder_GetBlockCatalog_DeduplicatesWhenSameAssemblyAddedTwice()
        {
            var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();

            // A second WithDi<> for the same assembly is a no-op in the plugin list, so distinct is trivially satisfied.
            var distinct = catalog.Distinct().ToList();
            Assert.HasCount(catalog.Count, distinct);
        }
    }
}