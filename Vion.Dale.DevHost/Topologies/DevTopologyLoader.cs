using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Topologies
{
    /// <summary>
    ///     Builds a <see cref="DevConfiguration" /> from a topology file (RFC 0006 R5): instance types are
    ///     resolved against the loaded assemblies (the DevHost project references its block libraries, so
    ///     they are loadable by full name), interface mappings are applied as declared, and contracts get
    ///     DevHost mocks exactly like a C#-preset build — explicit contract mappings, when present,
    ///     override the auto-created endpoints (the shared-contract case).
    /// </summary>
    public static class DevTopologyLoader
    {
        /// <summary>
        ///     Load a topology by id from a topologies directory (default <c>{cwd}/topologies</c>) into a
        ///     ready-to-build configuration. Throws <see cref="InvalidDataException" /> with every problem
        ///     when the file or its type references don't resolve.
        /// </summary>
        public static DevConfiguration Load(string id, string? topologiesDir = null)
        {
            var directory = DevDataDirectory.Resolve("topologies", topologiesDir);
            var path = Path.Combine(directory, id + DevTopologyFile.FileSuffix);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"No topology '{id}' under {directory}.", path);
            }

            return Build(DevTopologyFile.Load(path));
        }

        /// <summary>Build a configuration from an already-parsed topology file.</summary>
        public static DevConfiguration Build(DevTopologyFile topology)
        {
            var errors = new List<string>();
            var builder = DevConfigurationBuilder.Create().WithTopologyName(topology.Id!);
            var handles = new Dictionary<string, LogicBlockHandle>(StringComparer.Ordinal);

            foreach (var instance in topology.LogicBlockInstances!)
            {
                var type = ResolveType(instance.TypeFullName!);
                if (type is null)
                {
                    errors.Add($"type '{instance.TypeFullName}' is not loadable — the DevHost project must reference the library that declares it");
                    continue;
                }

                if (!typeof(LogicBlockBase).IsAssignableFrom(type))
                {
                    errors.Add($"type '{instance.TypeFullName}' is not a LogicBlockBase");
                    continue;
                }

                builder.AddLogicBlock(type, out var handle, instance.Name);
                handles[instance.Name!] = handle;
            }

            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", errors));
            }

            // Build first: blocks + the auto-mocked service providers (today's preset behavior).
            var configuration = builder.Build();

            // Interface mappings come from the file verbatim — the dev profile declares wiring
            // explicitly rather than re-running auto-discovery, so a file is reproducible by content.
            foreach (var mapping in topology.InterfaceMappings ?? Array.Empty<TopologyInterfaceMapping>())
            {
                var source = handles[mapping.SourceLogicBlockName!];
                var target = handles[mapping.TargetLogicBlockName!];
                configuration.InterfaceMappings.Add(new DevInterfaceMapping
                                                    {
                                                        SourceLogicBlockId = source.Id,
                                                        SourceLogicBlockName = source.Name,
                                                        SourceInterfaceIdentifier = mapping.SourceInterfaceIdentifier!,
                                                        TargetLogicBlockId = target.Id,
                                                        TargetLogicBlockName = target.Name,
                                                        TargetInterfaceIdentifier = mapping.TargetInterfaceIdentifier!,
                                                    });
            }

            // Explicit contract mappings override the auto-created defaults per (block, contract) —
            // how a file expresses shared endpoints. Unmentioned contracts keep their mocks.
            foreach (var mapping in topology.ContractMappings ?? Array.Empty<TopologyContractMapping>())
            {
                if (mapping.LogicBlockName is null || !handles.TryGetValue(mapping.LogicBlockName, out var handle))
                {
                    throw new InvalidDataException($"contractMappings: '{mapping.LogicBlockName}' is not a declared instance");
                }

                var block = configuration.LogicBlocks.Single(lb => lb.Id == handle.Id);
                var existing = block.ContractMappings.FirstOrDefault(cm => cm.ContractIdentifier == mapping.ContractIdentifier);
                if (existing is null)
                {
                    throw new InvalidDataException($"contractMappings: block '{mapping.LogicBlockName}' has no contract '{mapping.ContractIdentifier}'");
                }

                existing.ServiceProviderIdentifier = mapping.MappedServiceProviderIdentifier ?? existing.ServiceProviderIdentifier;
                existing.ServiceIdentifier = mapping.MappedServiceIdentifier ?? existing.ServiceIdentifier;
                existing.ContractEndpointIdentifier = mapping.MappedContractIdentifier ?? existing.ContractEndpointIdentifier;
            }

            return configuration;
        }

        // Full-name resolution against everything already loaded; Type.GetType additionally handles
        // assembly-qualified names. Lazy-loaded assemblies are the caller's concern — a DevHost
        // Program.cs references its block libraries, which are loaded by the time it wires DI.
        private static Type? ResolveType(string typeFullName)
        {
            var fromLoaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.GetType(typeFullName, false)).FirstOrDefault(t => t is not null);
            return fromLoaded ?? Type.GetType(typeFullName, false);
        }
    }

    /// <summary>Discovery over <c>{cwd}/topologies/*.topology.json</c> for the switching UI (rescan-on-read, like scenarios).</summary>
    public sealed class DevTopologyStore
    {
        public string Directory { get; }

        public DevTopologyStore(string? directory = null)
        {
            Directory = DevDataDirectory.Resolve("topologies", directory);
        }

        public IReadOnlyList<TopologyListEntry> List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<TopologyListEntry>();
            }

            return System.IO
                         .Directory
                         .EnumerateFiles(Directory, "*" + DevTopologyFile.FileSuffix)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                         .Select(path =>
                                 {
                                     var id = Path.GetFileName(path);
                                     id = id.Substring(0, id.Length - DevTopologyFile.FileSuffix.Length);
                                     try
                                     {
                                         var file = DevTopologyFile.Load(path);
                                         return new TopologyListEntry { Id = id, Blocks = file.LogicBlockInstances!.Count };
                                     }
                                     catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                                     {
                                         return new TopologyListEntry { Id = id, Error = e.Message };
                                     }
                                 })
                         .ToList();
        }
    }

    /// <summary>One discovered topology file for the switching UI.</summary>
    public sealed class TopologyListEntry
    {
        public string Id { get; set; } = string.Empty;

        public int Blocks { get; set; }

        public string? Error { get; set; }
    }
}