using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
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
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);

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
                types[instance.Name!] = type;
            }

            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", errors));
            }

            // Build first: blocks + the auto-mocked service providers (today's preset behavior).
            var configuration = builder.Build();

            // Interface mappings come from the file verbatim — the dev profile declares wiring
            // explicitly rather than re-running auto-discovery, so a file is reproducible by content.
            // Each authored mapping is also checked against the frozen MatchingInterface relation (RFC 0013
            // decision 1): an incompatible pair is recorded and reported, but the mapping is still applied so
            // the behaviour stays additive (the runtime tolerates it; the editor/validate flags it).
            var mappingErrors = new List<string>();
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

                // DiscoverMatchingInterfaces returns (source-interface-id, target-interface-id) pairs for
                // (sourceType, targetType); the names are guaranteed declared by DevTopologyFile.Parse.
                var pairs = DevConfigurationBuilder.DiscoverMatchingInterfaces(types[mapping.SourceLogicBlockName!], types[mapping.TargetLogicBlockName!]);
                if (!pairs.Contains((mapping.SourceInterfaceIdentifier!, mapping.TargetInterfaceIdentifier!)))
                {
                    mappingErrors.Add($"interfaceMappings: '{mapping.SourceLogicBlockName}.{mapping.SourceInterfaceIdentifier}' is not compatible with '{mapping.TargetLogicBlockName}.{mapping.TargetInterfaceIdentifier}'");
                }
            }

            if (mappingErrors.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", mappingErrors));
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
        // assembly-qualified names. When the declaring assembly is REFERENCED but not yet LOADED, fall back
        // to probing the application base directory, where a referenced library's assembly sits at build
        // time. This matters for an in-process host — especially an xunit test host, which does NOT eagerly
        // load a test project's references, so a file-backed topology that names blocks by typeFullName only
        // would otherwise resolve order-dependently (passing only if some other fixture happened to load the
        // library first). The probe removes that and the GC.KeepAlive ModuleInitializer shim a consumer would
        // otherwise need (DF-14). The DevHost app itself never reaches the fallback — it instantiates blocks
        // at startup, so their assemblies are already loaded.
        private static Type? ResolveType(string typeFullName)
        {
            var loaded = FindInLoadedAssemblies(typeFullName) ?? Type.GetType(typeFullName, false);
            if (loaded is not null)
            {
                return loaded;
            }

            foreach (var path in ProbeAssemblyPaths())
            {
                Assembly assembly;
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                }
                catch
                {
                    // Not a managed assembly loadable into the default context (native / resource dll, etc.).
                    continue;
                }

                var type = assembly.GetType(typeFullName, false);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type? FindInLoadedAssemblies(string typeFullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.GetType(typeFullName, false)).FirstOrDefault(t => t is not null);
        }

        // Managed assemblies in the app base directory that aren't already loaded — the referenced-but-unloaded
        // libraries an in-process host (especially a test host) may not have touched yet. The scan stops at the
        // first assembly that declares the wanted type, so the only-error path (genuinely missing type) is the
        // one that walks the whole directory.
        private static IEnumerable<string> ProbeAssemblyPaths()
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                return Array.Empty<string>();
            }

            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                try
                {
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        loadedPaths.Add(assembly.Location);
                    }
                }
                catch
                {
                    // In-memory assemblies throw on .Location — nothing to dedupe against, skip.
                }
            }

            return Directory.EnumerateFiles(baseDirectory, "*.dll").Where(path => !loadedPaths.Contains(path));
        }
    }

    /// <summary>
    ///     Discovery and persistence over <c>{cwd}/topologies/*.topology.json</c> for the switching UI and the
    ///     topology editor (RFC 0013) — rescan-on-read, like scenarios. Saving is path-confined to the directory
    ///     and disabled by <c>DALE_DEVHOST_READONLY_TOPOLOGIES=1</c>.
    /// </summary>
    public sealed class DevTopologyStore
    {
        /// <summary>Set to <c>1</c> to reject saves (<c>PUT /api/topologies/{id}</c>) — CI / locked-down checkouts.</summary>
        public const string ReadOnlyEnvVar = "DALE_DEVHOST_READONLY_TOPOLOGIES";

        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        public string Directory { get; }

        public static bool IsReadOnly
        {
            get => Environment.GetEnvironmentVariable(ReadOnlyEnvVar) == "1";
        }

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

        /// <summary>The raw file content for an id, or null when no such topology exists.</summary>
        public string? ReadRaw(string id)
        {
            if (!TryGetPath(id, out var path) || !ExistsExactCase(path))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // Deleted between the existence check and the read — absent, not an error.
                return null;
            }
        }

        /// <summary>
        ///     Save a topology from the editor (RFC 0013): the body is structurally parsed, the embedded id must
        ///     match, the catalog + interface-compatibility check (<see cref="DevTopologyLoader.Build" />) must
        ///     pass, and the write is confined to the topologies directory. Throws
        ///     <see cref="InvalidOperationException" /> when saving is disabled via <see cref="ReadOnlyEnvVar" />,
        ///     <see cref="InvalidDataException" /> for invalid content. Returns the absolute path written.
        /// </summary>
        public string Save(string id, string rawJson)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException($"Topology saving is disabled ({ReadOnlyEnvVar}=1).");
            }

            var file = DevTopologyFile.Parse(rawJson); // structural
            if (!string.Equals(file.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"id '{file.Id}' does not match the requested id '{id}'");
            }

            DevTopologyLoader.Build(file); // catalog + compatibility; throws InvalidDataException on any problem
            if (!TryGetPath(id, out var path))
            {
                throw new InvalidDataException($"'{id}' is not a valid topology id");
            }

            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, rawJson);
            return path;
        }

        // Windows file systems match names case-insensitively: without this check, 'Default' would load
        // default.topology.json locally and then 404 on Linux CI. Ordinal-exact name matching everywhere.
        private static bool ExistsExactCase(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            return directory is not null && System.IO.Directory.EnumerateFiles(directory, name).Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.Ordinal));
        }

        // Confinement: the id must be a slug (no separators, no dot-dot), and the combined path must stay
        // under the topologies directory — belt and suspenders against traversal.
        private bool TryGetPath(string id, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrEmpty(id) || !IdSlug.IsMatch(id) || id.Contains(".."))
            {
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(Directory, id + DevTopologyFile.FileSuffix));
            if (!candidate.StartsWith(Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = candidate;
            return true;
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