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
    ///     A parsed <c>*.topology.json</c> file — the dev profile of the
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

        private readonly IReadOnlyList<TopologyContractPairing>? _contractPairings;

        [JsonPropertyName("$schema")]
        public string? Schema { get; init; }

        public string? Id { get; init; }

        public IReadOnlyList<TopologyLogicBlockInstance>? LogicBlockInstances { get; init; }

        public IReadOnlyList<TopologyInterfaceMapping>? InterfaceMappings { get; init; }

        /// <summary>Optional explicit endpoint mappings (e.g. shared contracts); unlisted contracts are auto-mocked.</summary>
        public IReadOnlyList<TopologyContractMapping>? ContractMappings { get; init; }

        /// <summary>
        ///     Optional declarations that two service-provider contract endpoints are ONE wire — each
        ///     side's captured outbound is delivered as the other side's inbound, so a simulator block bound to a
        ///     provider face closes the loop a real service provider would close. Symmetric; which directions
        ///     actually materialise is derived from the two handlers' <c>[ScenarioWire]</c> declarations and
        ///     validated when the host loads the topology.
        ///     <para>
        ///         "No pairings" has exactly one spelling on the wire: the key is absent, never an empty array,
        ///         so a topology an editor added and then removed a pairing from saves byte-identically. The
        ///         getter collapses an empty list to none so every writer inherits that rather than repeating
        ///         the normalisation.
        ///     </para>
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<TopologyContractPairing>? ContractPairings
        {
            get => _contractPairings is { Count: > 0 } ? _contractPairings : null;

            init => _contractPairings = value;
        }

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

            // The same three id rules a scenario id carries (ScenarioFile.StructuralErrors): a slug, no
            // dot-dot, and not the reserved name the schema route already answers on. The dot-dot check is
            // load-bearing because DevTopologyLoader.Load combines an id with the directory unconfined, and
            // 'schema' would be shadowed by GET /api/topologies/schema.
            if (string.IsNullOrEmpty(file.Id) || !IdSlug.IsMatch(file.Id) || file.Id.Contains(".."))
            {
                errors.Add("id is required and must be a URL-safe slug ([A-Za-z0-9._-], starting alphanumeric, no '..')");
            }
            else if (string.Equals(file.Id, "schema", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("id 'schema' is reserved (GET /api/topologies/schema serves the format schema)");
            }

            if (file.LogicBlockInstances is null || file.LogicBlockInstances.Count == 0)
            {
                errors.Add("logicBlockInstances must declare at least one instance");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (instance, index) in (file.LogicBlockInstances ?? Array.Empty<TopologyLogicBlockInstance>()).Select((x, i) => (x, i)))
            {
                if (string.IsNullOrWhiteSpace(instance.TypeFullName))
                {
                    errors.Add($"logicBlockInstances[{index}]: typeFullName is required");
                }

                if (string.IsNullOrWhiteSpace(instance.Name))
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
                if (string.IsNullOrWhiteSpace(mapping.SourceLogicBlockName) || string.IsNullOrWhiteSpace(mapping.TargetLogicBlockName) ||
                    string.IsNullOrWhiteSpace(mapping.SourceInterfaceIdentifier) || string.IsNullOrWhiteSpace(mapping.TargetInterfaceIdentifier))
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

            foreach (var (pairing, index) in (file.ContractPairings ?? Array.Empty<TopologyContractPairing>()).Select((x, i) => (x, i)))
            {
                ValidatePairingEndpoint(pairing.A, $"contractPairings[{index}].a", names, errors);
                ValidatePairingEndpoint(pairing.B, $"contractPairings[{index}].b", names, errors);

                // Self-pairing is the dropped host-synthesised confirmation: an endpoint wired to
                // itself would echo every command back as its own confirmation, which is exactly the magic this
                // design replaces with a visible simulator block.
                if (pairing.A is not null && pairing.B is not null && pairing.A.LogicBlockName == pairing.B.LogicBlockName &&
                    pairing.A.ContractIdentifier == pairing.B.ContractIdentifier)
                {
                    errors.Add($"contractPairings[{index}]: both endpoints are '{pairing.A.LogicBlockName}.{pairing.A.ContractIdentifier}' — " +
                               "a pairing joins two distinct endpoints, and an echo back onto the same contract is a simulator block's job, not the host's");
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

                       // No empty-check needed: ContractPairings collapses an empty list to none itself.
                       ContractPairings = configuration.ContractPairings
                                                       .Select(cp => new TopologyContractPairing
                                                                     {
                                                                         A = new TopologyContractPairingEndpoint
                                                                             {
                                                                                 LogicBlockName = cp.A.LogicBlockName,
                                                                                 ContractIdentifier = cp.A.ContractIdentifier,
                                                                             },
                                                                         B = new TopologyContractPairingEndpoint
                                                                             {
                                                                                 LogicBlockName = cp.B.LogicBlockName,
                                                                                 ContractIdentifier = cp.B.ContractIdentifier,
                                                                             },
                                                                     })
                                                       .ToList(),
                   };
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, SerializerOptions);
        }

        private static void ValidatePairingEndpoint(TopologyContractPairingEndpoint? endpoint, string where, ICollection<string> declaredNames, ICollection<string> errors)
        {
            if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.LogicBlockName) || string.IsNullOrWhiteSpace(endpoint.ContractIdentifier))
            {
                errors.Add($"{where}: logicBlockName and contractIdentifier are both required");
                return;
            }

            if (!declaredNames.Contains(endpoint.LogicBlockName!))
            {
                errors.Add($"{where}: '{endpoint.LogicBlockName}' is not a declared instance");
            }
        }
    }

    public sealed class TopologyLogicBlockInstance
    {
        private readonly IReadOnlyDictionary<string, JsonNode>? _instantiationParameters;

        public string? TypeFullName { get; init; }

        public string? Name { get; init; }

        /// <summary>
        ///     Optional operator-chosen <c>[InstantiationParameter]</c> values (identifier → JSON
        ///     scalar) applied to the block before <c>Configure</c>, so config-time inclusion gates resolve.
        ///     Optional — an instance with no gated members needs none. Because parsing is strict on both
        ///     layers (<see cref="DevTopologyFile.SerializerOptions" /> and the JSON schema's
        ///     <c>additionalProperties: false</c>), the field is declared here and in every
        ///     <c>topology.schema.json</c> copy. Like <see cref="DevTopologyFile.ContractPairings" />, "no
        ///     parameters" has one spelling — the key absent, never an empty object — so instances without
        ///     parameters round-trip byte-identically (existing goldens unaffected).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, JsonNode>? InstantiationParameters
        {
            get => _instantiationParameters is { Count: > 0 } ? _instantiationParameters : null;

            init => _instantiationParameters = value;
        }
    }

    public sealed class TopologyInterfaceMapping
    {
        public string? SourceLogicBlockName { get; init; }

        public string? SourceInterfaceIdentifier { get; init; }

        public string? TargetLogicBlockName { get; init; }

        public string? TargetInterfaceIdentifier { get; init; }
    }

    /// <summary>
    ///     Two service-provider contract endpoints declared as one wire. Keyed on (block,
    ///     contract binding), never on endpoint triples — every contract already has an auto-created endpoint,
    ///     so a pairing needs nothing from <c>contractMappings</c>. The declaration is symmetric; the host
    ///     derives which directions materialise from the two handlers' <c>[ScenarioWire]</c> types.
    /// </summary>
    public sealed class TopologyContractPairing
    {
        public TopologyContractPairingEndpoint? A { get; init; }

        public TopologyContractPairingEndpoint? B { get; init; }
    }

    /// <summary>One side of a <see cref="TopologyContractPairing" /> — a contract binding on a declared instance.</summary>
    public sealed class TopologyContractPairingEndpoint
    {
        public string? LogicBlockName { get; init; }

        public string? ContractIdentifier { get; init; }
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