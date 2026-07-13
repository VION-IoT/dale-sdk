using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Topologies
{
    /// <summary>
    ///     A parsed <c>*.topology.json</c> file (RFC 0006 "Topology files") — the dev profile of the
    ///     production <c>SetLogicConfigurationPayload</c>: logic-block instances (type full name + instance
    ///     name) and interface mappings, without deployment concerns (MQTT topics, package pinning).
    ///     Contract mappings are optional — contracts left unmapped get DevHost mocks, exactly the
    ///     C#-preset behavior. Scenario files reference topologies by id; <c>dale dev --export-topology</c>
    ///     dumps a C#-built preset in this shape (the migration path).
    /// </summary>
    public sealed class DevTopologyFile
    {
        public const string FileSuffix = ".topology.json";

        /// <summary>
        ///     The conventional, per-project <c>$schema</c> reference emitted on export — a sibling
        ///     <c>.dale/topology.schema.json</c> (the generic schema this package ships and serves at
        ///     <c>GET /api/topologies/schema</c>). Editors that resolve it then catch wrong field names in
        ///     hand edits (parsing is strict). Mirrors the scenario files' <c>./.dale/scenario.schema.json</c>.
        /// </summary>
        public const string SchemaRef = "./.dale/topology.schema.json";

        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        internal static readonly JsonSerializerOptions SerializerOptions = new()
                                                                           {
                                                                               PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                               UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                                                                               AllowDuplicateProperties = false,
                                                                               WriteIndented = true,
                                                                           };

        [JsonPropertyName("$schema")]
        public string? Schema { get; init; }

        public string? Id { get; init; }

        public IReadOnlyList<TopologyLogicBlockInstance>? LogicBlockInstances { get; init; }

        public IReadOnlyList<TopologyInterfaceMapping>? InterfaceMappings { get; init; }

        /// <summary>Optional explicit endpoint mappings (e.g. shared contracts); unlisted contracts are auto-mocked.</summary>
        public IReadOnlyList<TopologyContractMapping>? ContractMappings { get; init; }

        /// <summary>Parse and structurally validate topology JSON, throwing with every problem at once.</summary>
        public static DevTopologyFile Parse(string json)
        {
            DevTopologyFile? file;
            try
            {
                file = JsonSerializer.Deserialize<DevTopologyFile>(json, SerializerOptions);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"not valid topology JSON: {e.Message}");
            }

            if (file is null)
            {
                throw new InvalidDataException("not valid topology JSON: document is null");
            }

            var errors = new List<string>();
            if (string.IsNullOrEmpty(file.Id) || !IdSlug.IsMatch(file.Id))
            {
                errors.Add("id is required and must be a URL-safe slug");
            }

            if (file.LogicBlockInstances is null || file.LogicBlockInstances.Count == 0)
            {
                errors.Add("logicBlockInstances must declare at least one instance");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (instance, index) in (file.LogicBlockInstances ?? Array.Empty<TopologyLogicBlockInstance>()).Select((x, i) => (x, i)))
            {
                if (string.IsNullOrEmpty(instance.TypeFullName))
                {
                    errors.Add($"logicBlockInstances[{index}]: typeFullName is required");
                }

                if (string.IsNullOrEmpty(instance.Name))
                {
                    errors.Add($"logicBlockInstances[{index}]: name is required");
                }
                else if (!names.Add(instance.Name))
                {
                    errors.Add($"logicBlockInstances[{index}]: duplicate instance name '{instance.Name}'");
                }
                else if (instance.Name.Contains('.'))
                {
                    errors.Add($"logicBlockInstances[{index}]: instance names must not contain '.' (scenario name paths split on it)");
                }
            }

            foreach (var (mapping, index) in (file.InterfaceMappings ?? Array.Empty<TopologyInterfaceMapping>()).Select((x, i) => (x, i)))
            {
                if (string.IsNullOrEmpty(mapping.SourceLogicBlockName) || string.IsNullOrEmpty(mapping.TargetLogicBlockName) ||
                    string.IsNullOrEmpty(mapping.SourceInterfaceIdentifier) || string.IsNullOrEmpty(mapping.TargetInterfaceIdentifier))
                {
                    errors.Add($"interfaceMappings[{index}]: sourceLogicBlockName, sourceInterfaceIdentifier, targetLogicBlockName, targetInterfaceIdentifier are all required");
                }
                else
                {
                    if (!names.Contains(mapping.SourceLogicBlockName))
                    {
                        errors.Add($"interfaceMappings[{index}]: '{mapping.SourceLogicBlockName}' is not a declared instance");
                    }

                    if (!names.Contains(mapping.TargetLogicBlockName))
                    {
                        errors.Add($"interfaceMappings[{index}]: '{mapping.TargetLogicBlockName}' is not a declared instance");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", errors));
            }

            return file;
        }

        /// <summary>Load and parse a topology file; the id must match the file name (<c>&lt;id&gt;.topology.json</c>).</summary>
        public static DevTopologyFile Load(string path)
        {
            var file = Parse(File.ReadAllText(path));
            var expectedId = Path.GetFileName(path);
            if (expectedId.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                expectedId = expectedId.Substring(0, expectedId.Length - FileSuffix.Length);
            }

            if (!string.Equals(file.Id, expectedId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"id '{file.Id}' does not match the file name (expected '{expectedId}')");
            }

            return file;
        }

        /// <summary>
        ///     Project a wired host's configuration into the dev-profile shape — the
        ///     <c>dale dev --export-topology</c> migration path from C# presets to topology files.
        ///     Contract mappings are exported for fidelity; consumers can prune them back to the
        ///     auto-mock default.
        /// </summary>
        public static DevTopologyFile FromConfiguration(ConfigurationOutput configuration)
        {
            return new DevTopologyFile
                   {
                       Schema = SchemaRef,
                       Id = configuration.TopologyName ?? "default",
                       LogicBlockInstances = configuration.LogicBlocks
                                                          .Select(lb => new TopologyLogicBlockInstance
                                                                        {
                                                                            TypeFullName = lb.TypeFullName,
                                                                            Name = lb.Name,
                                                                            InstantiationParameters = lb.InstantiationParameters,
                                                                        })
                                                          .ToList(),
                       InterfaceMappings = configuration.InterfaceMappings
                                                        .Select(im => new TopologyInterfaceMapping
                                                                      {
                                                                          SourceLogicBlockName = im.SourceLogicBlockName,
                                                                          SourceInterfaceIdentifier = im.SourceInterfaceIdentifier,
                                                                          TargetLogicBlockName = im.TargetLogicBlockName,
                                                                          TargetInterfaceIdentifier = im.TargetInterfaceIdentifier,
                                                                      })
                                                        .ToList(),
                       ContractMappings = configuration.LogicBlocks
                                                       .SelectMany(lb => lb.ContractMappings.Select(cm => new TopologyContractMapping
                                                                                                          {
                                                                                                              LogicBlockName = lb.Name,
                                                                                                              ContractIdentifier = cm.ContractIdentifier,
                                                                                                              MappedServiceProviderIdentifier = cm.MappedServiceProviderIdentifier,
                                                                                                              MappedServiceIdentifier = cm.MappedServiceIdentifier,
                                                                                                              MappedContractIdentifier = cm.MappedContractIdentifier,
                                                                                                          }))
                                                       .ToList(),
                   };
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, SerializerOptions);
        }
    }

    public sealed class TopologyLogicBlockInstance
    {
        public string? TypeFullName { get; init; }

        public string? Name { get; init; }

        /// <summary>
        ///     RFC 0016: optional operator-chosen <c>[InstantiationParameter]</c> values (identifier → JSON
        ///     scalar) applied to the block before <c>Configure</c>, so config-time inclusion gates resolve.
        ///     Optional — an instance with no gated members needs none. Because parsing is strict on both
        ///     layers (<see cref="DevTopologyFile.SerializerOptions" /> and the JSON schema's
        ///     <c>additionalProperties: false</c>), the field is declared here and in every
        ///     <c>topology.schema.json</c> copy. Omitted from serialization when null so instances without
        ///     parameters round-trip byte-identically (existing goldens unaffected).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, JsonNode>? InstantiationParameters { get; init; }
    }

    public sealed class TopologyInterfaceMapping
    {
        public string? SourceLogicBlockName { get; init; }

        public string? SourceInterfaceIdentifier { get; init; }

        public string? TargetLogicBlockName { get; init; }

        public string? TargetInterfaceIdentifier { get; init; }
    }

    public sealed class TopologyContractMapping
    {
        public string? LogicBlockName { get; init; }

        public string? ContractIdentifier { get; init; }

        // Field names converged with ConfigurationOutput.ContractMapping (the `dale dev --export-config`
        // shape) so the two near-identical topology/config JSON forms no longer diverge (DF-11).
        public string? MappedServiceProviderIdentifier { get; init; }

        public string? MappedServiceIdentifier { get; init; }

        public string? MappedContractIdentifier { get; init; }
    }
}