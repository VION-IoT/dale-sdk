using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     <see cref="DevHostWebRunner.ResolveBootTopologyId" /> and the
    ///     <see cref="DevHostWebRunner.RunFolderDrivenAsync" /> factory logic: boot resolution, auto-gen
    ///     fallback, GetBlockCatalog + Build on the same builder, and a light integration check.
    /// </summary>
    [TestClass]
    public class FolderDrivenBootShould
    {
        // ──────────────────────────────────────────────────────────────────────────────
        // ResolveBootTopologyId — the three resolution cases
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ResolveBootTopologyId_EmptyDir_WritesDefaultAndReturnsDefault()
        {
            var dir = TempDir();
            try
            {
                // Pre-condition: the directory exists but is empty (no topology files).
                Directory.CreateDirectory(dir);

                var catalog = new[] { typeof(CounterBlock) };
                var id = DevHostWebRunner.ResolveBootTopologyId(catalog, dir);

                Assert.AreEqual("default", id);

                var expectedFile = Path.Combine(dir, "default" + DevTopologyFile.FileSuffix);
                Assert.IsTrue(File.Exists(expectedFile), "default.topology.json should be written to the topologies directory");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [TestMethod]
        public void ResolveBootTopologyId_DirWithDefault_ReturnsDefaultWithoutRegenerating()
        {
            var dir = TempDir();
            try
            {
                Directory.CreateDirectory(dir);

                // Write a sentinel default topology.
                var defaultPath = Path.Combine(dir, "default" + DevTopologyFile.FileSuffix);
                var sentinelContent = """
                                      {
                                        "id": "default",
                                        "logicBlockInstances": [ { "typeFullName": "Vion.Dale.DevHost.Test.CounterBlock", "name": "CounterBlock" } ]
                                      }
                                      """;
                File.WriteAllText(defaultPath, sentinelContent);

                var catalog = new[] { typeof(SourceBlock), typeof(SinkBlock) };
                var id = DevHostWebRunner.ResolveBootTopologyId(catalog, dir);

                Assert.AreEqual("default", id);

                // File must not be regenerated — sentinel content stays intact.
                Assert.AreEqual(sentinelContent, File.ReadAllText(defaultPath), "existing default.topology.json must not be overwritten");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [TestMethod]
        public void ResolveBootTopologyId_MultipleFilesNoDefault_ReturnsFirstAlphabetically()
        {
            var dir = TempDir();
            try
            {
                Directory.CreateDirectory(dir);

                // Write two topologies in non-alphabetical creation order — only the alphabetical first should win.
                File.WriteAllText(
                    Path.Combine(dir, "zzz" + DevTopologyFile.FileSuffix),
                    """
                    {
                      "id": "zzz",
                      "logicBlockInstances": [ { "typeFullName": "Vion.Dale.DevHost.Test.SinkBlock", "name": "SinkBlock" } ]
                    }
                    """);

                File.WriteAllText(
                    Path.Combine(dir, "aaa" + DevTopologyFile.FileSuffix),
                    """
                    {
                      "id": "aaa",
                      "logicBlockInstances": [ { "typeFullName": "Vion.Dale.DevHost.Test.SourceBlock", "name": "SourceBlock" } ]
                    }
                    """);

                var catalog = new[] { typeof(CounterBlock) };
                var id = DevHostWebRunner.ResolveBootTopologyId(catalog, dir);

                Assert.AreEqual("aaa", id, "first topology alphabetically should be selected when no 'default' exists");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // GetBlockCatalog + Build on the same builder must work (no double-consume)
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task DevHostBuilder_GetBlockCatalog_ThenBuild_WorksOnSameInstance()
        {
            // Arrange: a builder whose catalog is inspected first (as RunFolderDrivenAsync does internally)
            // and then used to build a real host.  This guards the "temp ServiceCollection" approach in
            // GetBlockCatalog() — it must NOT mutate the builder's real _services.
            var topologiesDir = TempDir();
            try
            {
                Directory.CreateDirectory(topologiesDir);

                var builder = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>();

                // Step 1: enumerate the catalog (must not consume/corrupt the builder).
                var catalog = builder.GetBlockCatalog();
                Assert.IsGreaterThanOrEqualTo(2, catalog.Count, "CrossBlockDependencyInjection registers at least SourceBlock + SinkBlock");

                // Step 2: write a topology for the builder to load (avoids dependency on auto-gen paths).
                var topoPath = Path.Combine(topologiesDir, "default" + DevTopologyFile.FileSuffix);
                File.WriteAllText(topoPath, $$"""
                                             {
                                               "id": "default",
                                               "logicBlockInstances": [
                                                 { "typeFullName": "{{typeof(SourceBlock).FullName}}", "name": "source" },
                                                 { "typeFullName": "{{typeof(SinkBlock).FullName}}", "name": "sink" }
                                               ],
                                               "interfaceMappings": [
                                                 { "sourceLogicBlockName": "source", "sourceInterfaceIdentifier": "ISource",
                                                   "targetLogicBlockName": "sink", "targetInterfaceIdentifier": "ISink" }
                                               ]
                                             }
                                             """);

                // Step 3: configure + build on the SAME builder — must not throw.
                var config = DevTopologyLoader.Load("default", topologiesDir);
                builder.WithConfiguration(config);
                await using var host = builder.Build();

                Assert.AreEqual("default", host.Control.GetConfiguration().TopologyName);
                CollectionAssert.Contains(
                    host.Control.GetConfiguration().LogicBlocks.Select(lb => lb.Name).ToList(),
                    "source");
            }
            finally
            {
                DeleteDir(topologiesDir);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // Light integration: factory logic resolves + builds a correct host
        // ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task FolderDriven_BootsWithCommittedTopologyAndBlocksArePresent()
        {
            // Simulate what RunFolderDrivenAsync does internally: resolve + build + verify the host.
            var topologiesDir = TempDir();
            try
            {
                Directory.CreateDirectory(topologiesDir);

                // Commit a hand-written topology — should take priority over auto-gen.
                File.WriteAllText(
                    Path.Combine(topologiesDir, "default" + DevTopologyFile.FileSuffix),
                    $$"""
                      {
                        "id": "default",
                        "logicBlockInstances": [
                          { "typeFullName": "{{typeof(SourceBlock).FullName}}", "name": "source" },
                          { "typeFullName": "{{typeof(SinkBlock).FullName}}", "name": "sink" }
                        ],
                        "interfaceMappings": [
                          { "sourceLogicBlockName": "source", "sourceInterfaceIdentifier": "ISource",
                            "targetLogicBlockName": "sink", "targetInterfaceIdentifier": "ISink" }
                        ]
                      }
                      """);

                var catalog = DevHostBuilder.Create().WithDi<CrossBlockDependencyInjection>().GetBlockCatalog();
                var bootId = DevHostWebRunner.ResolveBootTopologyId(catalog, topologiesDir);
                Assert.AreEqual("default", bootId);

                var config = DevTopologyLoader.Load(bootId, topologiesDir);
                await using var host = DevHostBuilder.Create()
                                                     .WithDi<CrossBlockDependencyInjection>()
                                                     .WithConfiguration(config)
                                                     .Build();

                await host.StartAsync(CancellationToken.None);

                var cfg = host.Control.GetConfiguration();
                Assert.AreEqual("default", cfg.TopologyName);
                CollectionAssert.Contains(cfg.LogicBlocks.Select(lb => lb.Name).ToList(), "source");
                CollectionAssert.Contains(cfg.LogicBlocks.Select(lb => lb.Name).ToList(), "sink");
            }
            finally
            {
                DeleteDir(topologiesDir);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────────

        private static string TempDir()
        {
            return Path.Combine(Path.GetTempPath(), "dale-folder-driven-" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
