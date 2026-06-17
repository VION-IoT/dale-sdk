using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vion.Dale.DevHost.Topologies
{
    /// <summary>
    ///     Generates a default topology from a catalog of <see cref="Sdk.Core.LogicBlockBase" /> types:
    ///     each type instantiated once, all unambiguous interface pairs auto-connected. Intended as a
    ///     boot-time fallback when no hand-authored topology exists — developers can edit or commit the
    ///     generated file, or gitignore it.
    /// </summary>
    public static class DefaultTopologyGenerator
    {
        /// <summary>
        ///     Build a <see cref="DevConfiguration" /> with one instance per block type in
        ///     <paramref name="blockTypes" />, auto-connected.
        ///     <para>
        ///         Instance name = the type's simple name (e.g. <c>CounterBlock</c>), matching the
        ///         convention of today's C# presets.
        ///     </para>
        ///     <para>
        ///         // TODO(phase3): AutoConnect over an uncurated catalog is best-effort — two blocks that
        ///         both implement a "commander" side of the same interface (e.g. two blocks that both manage
        ///         the same device) will be wired into a potentially fighting network. Curated libraries
        ///         should commit their own topology files so this generator never runs for them; a
        ///         conflict-detection pass should be added here for uncurated catalogs before this path is
        ///         used in production boot.
        ///     </para>
        /// </summary>
        public static DevConfiguration Generate(IEnumerable<Type> blockTypes, string id = "default")
        {
            var builder = DevConfigurationBuilder.Create().WithTopologyName(id);

            foreach (var type in blockTypes)
            {
                builder.AddLogicBlock(type, out _, type.Name);
            }

            // TODO(phase3): AutoConnect over an uncurated catalog is best-effort — two blocks that both
            // manage the same device will be wired into a fighting network. Curated libraries commit their
            // own topology files so this generator never runs for them. Add conflict-detection before this
            // path is used in production boot.
            builder.AutoConnect();

            return builder.Build();
        }

        /// <summary>
        ///     Project a <see cref="DevConfiguration" /> (built by <see cref="Generate" /> or a C# preset)
        ///     into the on-disk <see cref="DevTopologyFile" /> shape, ready to serialize and round-trip via
        ///     <see cref="DevTopologyLoader.Build" />.
        /// </summary>
        public static DevTopologyFile ToTopologyFile(DevConfiguration config)
        {
            return new DevTopologyFile
                   {
                       Schema = DevTopologyFile.SchemaRef,
                       Id = config.TopologyName ?? "default",
                       LogicBlockInstances = config.LogicBlocks
                                                   .Select(lb => new TopologyLogicBlockInstance
                                                                 {
                                                                     TypeFullName = lb.LogicBlockType.FullName,
                                                                     Name = lb.Name,
                                                                 })
                                                   .ToList(),
                       InterfaceMappings = config.InterfaceMappings
                                                 .Select(im => new TopologyInterfaceMapping
                                                               {
                                                                   SourceLogicBlockName = im.SourceLogicBlockName,
                                                                   SourceInterfaceIdentifier = im.SourceInterfaceIdentifier,
                                                                   TargetLogicBlockName = im.TargetLogicBlockName,
                                                                   TargetInterfaceIdentifier = im.TargetInterfaceIdentifier,
                                                               })
                                                 .ToList(),

                       // Contract mappings are omitted from the default topology: the loader auto-mocks
                       // them, exactly matching the C# preset behavior. Consumers can add explicit mappings
                       // to the written file to express shared-endpoint wiring.
                       ContractMappings = Array.Empty<TopologyContractMapping>(),
                   };
        }

        /// <summary>
        ///     Generate a default topology from <paramref name="blockTypes" />, write it to
        ///     <c>&lt;topologiesDir&gt;/&lt;id&gt;.topology.json</c>, and return the written path.
        ///     <para>
        ///         If a file with that id already exists the existing path is returned unchanged — this is a
        ///         fallback generator, not a clobber. Developers who have committed or edited the file keep
        ///         their version.
        ///     </para>
        /// </summary>
        /// <param name="blockTypes">The catalog of block types to instantiate.</param>
        /// <param name="topologiesDir">
        ///     Explicit topology directory; resolved by <see cref="DevDataDirectory.Resolve" /> when null.
        /// </param>
        /// <param name="id">Topology id and base file name (default: <c>"default"</c>).</param>
        /// <returns>Absolute path of the (written or pre-existing) topology file.</returns>
        public static string WriteDefault(IEnumerable<Type> blockTypes, string? topologiesDir = null, string id = "default")
        {
            var directory = DevDataDirectory.Resolve("topologies", topologiesDir);
            var path = Path.Combine(directory, id + DevTopologyFile.FileSuffix);

            if (File.Exists(path))
            {
                return path;
            }

            var config = Generate(blockTypes, id);
            var file = ToTopologyFile(config);
            var json = JsonSerializer.Serialize(file, DevTopologyFile.SerializerOptions);

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);

            return path;
        }
    }
}