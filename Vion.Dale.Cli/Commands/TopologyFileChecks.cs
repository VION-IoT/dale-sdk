using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vion.Dale.Cli.Commands
{
    /// <summary>Outcome of validating one topology file.</summary>
    public sealed class TopologyCheckOutcome
    {
        /// <summary>Problems that make the file invalid; a non-empty list fails the command.</summary>
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        /// <summary>Problems the loader tolerates; reported, but the file still passes.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    ///     The language-neutral validation core behind <c>dale topology validate</c>: a deliberately lite
    ///     mirror of the rules <c>DevTopologyFile</c> applies when the host loads a <c>*.topology.json</c>,
    ///     evaluated over raw JSON. The DevHost's loader stays authoritative — this exists so a hand edit
    ///     is caught in a PR lane without booting a host per file, the same bargain
    ///     <see cref="ScenarioFileChecks" /> strikes for scenario files.
    ///     <para>
    ///         Three of the loader's rules are deliberately not mirrored, because none of them is decidable
    ///         from the file alone: whether an instance's type is loadable and is a logic block, whether a
    ///         contract mapping names a contract the block actually carries, and which directions of a
    ///         pairing materialise (CLR type identity of the two handlers' <c>[ScenarioWire]</c> halves).
    ///         Those stay the host's, and a consumer's stepped-scenario gate is where the files are proven
    ///         to load.
    ///     </para>
    /// </summary>
    public static class TopologyFileChecks
    {
        /// <summary>The file suffix the loader keys an id on — <c>DevTopologyFile.FileSuffix</c>.</summary>
        public const string FileSuffix = ".topology.json";

        /// <summary>
        ///     The conventional per-project <c>$schema</c> reference, restated from
        ///     <c>DevTopologyFile.SchemaRef</c> because this validator deliberately does not reference
        ///     Vion.Dale.DevHost. The definition-site agreement test compares the two.
        /// </summary>
        internal const string DevTopologySchemaRef = "./.dale/topology.schema.json";

        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        // Duplicate members are refused at the source, matching the host's SerializerOptions
        // (AllowDuplicateProperties = false). JsonNodeOptions carries no such knob and JsonNode keeps the
        // last of a repeated member silently, so the document is parsed once for this check alone — a file
        // whose second "id" contradicts its first would otherwise pass here and fail at load.
        private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

        // The declared members of each object level, mirroring both the strict parser
        // (UnmappedMemberHandling.Disallow) and the schema's `additionalProperties: false`. A member absent
        // from the level's set is what a hand edit's misspelling looks like.
        private static readonly string[] RootMembers = ["$schema", "id", "logicBlockInstances", "interfaceMappings", "contractPairings", "contractMappings"];

        private static readonly string[] InstanceMembers = ["typeFullName", "name", "instantiationParameters"];

        private static readonly string[] InterfaceMappingMembers = ["sourceLogicBlockName", "sourceInterfaceIdentifier", "targetLogicBlockName", "targetInterfaceIdentifier"];

        private static readonly string[] PairingMembers = ["a", "b"];

        private static readonly string[] PairingEndpointMembers = ["logicBlockName", "contractIdentifier"];

        private static readonly string[] ContractMappingMembers =
            ["logicBlockName", "contractIdentifier", "mappedServiceProviderIdentifier", "mappedServiceIdentifier", "mappedContractIdentifier"];

        /// <summary>
        ///     Validate one topology file's text, reporting every problem at once rather than the first.
        ///     <paramref name="fileName" /> is the name the id must match; <paramref name="requireSchemaRef" />
        ///     promotes the missing-<c>$schema</c> warning to an error, which the loader never does.
        /// </summary>
        public static TopologyCheckOutcome Validate(string fileName, string json, bool requireSchemaRef = false)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            JsonNode? root;
            try
            {
                using (JsonDocument.Parse(json, DocumentOptions))
                {
                }

                root = JsonNode.Parse(json);
            }
            catch (JsonException e)
            {
                return new TopologyCheckOutcome { Errors = new[] { $"not valid topology JSON: {e.Message}" } };
            }

            if (root is not JsonObject topology)
            {
                return new TopologyCheckOutcome { Errors = new[] { "not a JSON object" } };
            }

            CheckMembers(topology, RootMembers, null, errors);

            if (topology["$schema"] is { } schemaRef)
            {
                if (AsString(schemaRef) is null)
                {
                    errors.Add("$schema must be a string");
                }
            }
            else
            {
                var message = $"no '$schema' reference (conventionally \"{DevTopologySchemaRef}\", which is what makes an editor check a hand edit)";
                (requireSchemaRef ? errors : warnings).Add(message);
            }

            // The same three id rules the loader applies, then the file-name match it applies separately.
            var id = AsString(topology["id"]);
            var expectedId = fileName.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^FileSuffix.Length] : fileName;
            if (id is null || !IdSlug.IsMatch(id) || id.Contains(".."))
            {
                errors.Add("id is required and must be a URL-safe slug ([A-Za-z0-9._-], starting alphanumeric, no '..')");
            }
            else if (string.Equals(id, "schema", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("id 'schema' is reserved (GET /api/topologies/schema serves the format schema)");
            }
            else if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                errors.Add($"id '{id}' does not match the file name (expected '{expectedId}')");
            }

            var names = ValidateInstances(topology, errors);
            ValidateInterfaceMappings(topology, names, errors);
            ValidateContractPairings(topology, names, errors);
            ValidateContractMappings(topology, names, errors);

            return new TopologyCheckOutcome { Errors = errors, Warnings = warnings };
        }

        // Returns the declared instance names, which the mapping and pairing checks resolve against. An
        // instance whose name is missing, duplicated or dotted contributes nothing to the set, so a mapping
        // that names it is reported too — one broken instance, two messages, which is what the loader does.
        private static HashSet<string> ValidateInstances(JsonObject topology, ICollection<string> errors)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var instances = topology["logicBlockInstances"];
            if (instances is not null && instances is not JsonArray)
            {
                errors.Add("logicBlockInstances must be an array");
                return names;
            }

            if (instances is not JsonArray { Count: > 0 } declared)
            {
                errors.Add("logicBlockInstances must declare at least one instance");
                return names;
            }

            for (var index = 0; index < declared.Count; index++)
            {
                var where = $"logicBlockInstances[{index}]";
                if (declared[index] is not JsonObject instance)
                {
                    errors.Add($"{where}: not a JSON object");
                    continue;
                }

                CheckMembers(instance, InstanceMembers, where, errors);

                if (string.IsNullOrWhiteSpace(AsString(instance["typeFullName"])))
                {
                    errors.Add($"{where}: typeFullName is required");
                }

                var name = AsString(instance["name"]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"{where}: name is required");
                }
                else if (!names.Add(name!))
                {
                    errors.Add($"{where}: duplicate instance name '{name}'");
                }
                else if (name!.Contains('.'))
                {
                    errors.Add($"{where}: instance names must not contain '.' (scenario name paths split on it)");
                }

                ValidateInstantiationParameters(instance["instantiationParameters"], where, errors);
            }

            return names;
        }

        // The schema admits any JSON scalar here and nothing else, because the value is decoded against the
        // parameter's own declared type. An object or an array is a shape no parameter type decodes, and it
        // reaches the operator as a config-time gate that silently did not resolve.
        private static void ValidateInstantiationParameters(JsonNode? parameters, string where, ICollection<string> errors)
        {
            if (parameters is null)
            {
                return;
            }

            if (parameters is not JsonObject declared)
            {
                errors.Add($"{where}: instantiationParameters must be an object of identifier → JSON scalar");
                return;
            }

            foreach (var (identifier, value) in declared)
            {
                if (value is JsonObject or JsonArray)
                {
                    errors.Add($"{where}: instantiationParameters['{identifier}'] must be a JSON scalar (boolean, number, string or null)");
                }
            }
        }

        private static void ValidateInterfaceMappings(JsonObject topology, ICollection<string> names, ICollection<string> errors)
        {
            if (OptionalArray(topology, "interfaceMappings", errors) is not { } mappings)
            {
                return;
            }

            for (var index = 0; index < mappings.Count; index++)
            {
                var where = $"interfaceMappings[{index}]";
                if (mappings[index] is not JsonObject mapping)
                {
                    errors.Add($"{where}: not a JSON object");
                    continue;
                }

                CheckMembers(mapping, InterfaceMappingMembers, where, errors);

                var source = AsString(mapping["sourceLogicBlockName"]);
                var target = AsString(mapping["targetLogicBlockName"]);
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(AsString(mapping["sourceInterfaceIdentifier"])) ||
                    string.IsNullOrWhiteSpace(AsString(mapping["targetInterfaceIdentifier"])))
                {
                    errors.Add($"{where}: sourceLogicBlockName, sourceInterfaceIdentifier, targetLogicBlockName, targetInterfaceIdentifier are all required");
                    continue;
                }

                if (!names.Contains(source!))
                {
                    errors.Add($"{where}: '{source}' is not a declared instance");
                }

                if (!names.Contains(target!))
                {
                    errors.Add($"{where}: '{target}' is not a declared instance");
                }
            }
        }

        private static void ValidateContractPairings(JsonObject topology, ICollection<string> names, ICollection<string> errors)
        {
            if (OptionalArray(topology, "contractPairings", errors) is not { } pairings)
            {
                return;
            }

            var wires = new HashSet<(string, string)>();
            for (var index = 0; index < pairings.Count; index++)
            {
                var where = $"contractPairings[{index}]";
                if (pairings[index] is not JsonObject pairing)
                {
                    errors.Add($"{where}: not a JSON object");
                    continue;
                }

                CheckMembers(pairing, PairingMembers, where, errors);

                var a = ValidatePairingEndpoint(pairing["a"], $"{where}.a", names, errors);
                var b = ValidatePairingEndpoint(pairing["b"], $"{where}.b", names, errors);
                if (a is null || b is null)
                {
                    continue;
                }

                // Self-pairing is the dropped host-synthesised confirmation: an endpoint wired to itself
                // would echo every command back as its own confirmation, which is exactly the magic the
                // pairing design replaces with a visible simulator block.
                if (string.Equals(a, b, StringComparison.Ordinal))
                {
                    errors.Add($"{where}: both endpoints are '{a}' — a pairing joins two distinct endpoints, and an echo back onto the same contract " +
                               "is a simulator block's job, not the host's");
                    continue;
                }

                // A pairing is symmetric, so a-to-b and b-to-a are the same wire, and a repeat installs the
                // same forward twice — which the host refuses when it builds the topology. The two endpoints
                // are held as an ordered pair rather than joined into one key: a contract identifier may
                // carry a '.', so any separator risks two distinct wires colliding on one string.
                var wire = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
                if (!wires.Add(wire))
                {
                    errors.Add($"{where}: '{a}' and '{b}' are already paired — a pairing is symmetric, so declare it once");
                }
            }
        }

        // Returns the endpoint as `Block.Contract`, or null when it is malformed (the caller then has
        // nothing to pair or to compare).
        private static string? ValidatePairingEndpoint(JsonNode? endpoint, string where, ICollection<string> names, ICollection<string> errors)
        {
            if (endpoint is not JsonObject side)
            {
                errors.Add($"{where}: logicBlockName and contractIdentifier are both required");
                return null;
            }

            CheckMembers(side, PairingEndpointMembers, where, errors);

            var block = AsString(side["logicBlockName"]);
            var contract = AsString(side["contractIdentifier"]);
            if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(contract))
            {
                errors.Add($"{where}: logicBlockName and contractIdentifier are both required");
                return null;
            }

            if (!names.Contains(block!))
            {
                errors.Add($"{where}: '{block}' is not a declared instance");
            }

            return $"{block}.{contract}";
        }

        // Both halves the loader decides from the file: a mapping names a block and a contract binding, and
        // the block it names is a declared instance. Whether that block carries that contract needs the loaded
        // catalog, so only that half stays the host's.
        private static void ValidateContractMappings(JsonObject topology, ICollection<string> names, ICollection<string> errors)
        {
            if (OptionalArray(topology, "contractMappings", errors) is not { } mappings)
            {
                return;
            }

            for (var index = 0; index < mappings.Count; index++)
            {
                var where = $"contractMappings[{index}]";
                if (mappings[index] is not JsonObject mapping)
                {
                    errors.Add($"{where}: not a JSON object");
                    continue;
                }

                CheckMembers(mapping, ContractMappingMembers, where, errors);

                var block = AsString(mapping["logicBlockName"]);
                if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(AsString(mapping["contractIdentifier"])))
                {
                    errors.Add($"{where}: logicBlockName and contractIdentifier are both required");
                }
                else if (!names.Contains(block!))
                {
                    errors.Add($"{where}: '{block}' is not a declared instance");
                }
            }
        }

        // A collection of the wrong JSON kind is reported, never skipped: the host's strict deserializer
        // throws on it, and a validator that quietly returns here would pass the file AND silently drop every
        // check over that collection — the shape a hand edit takes when the brackets around a single entry go.
        private static JsonArray? OptionalArray(JsonObject topology, string member, ICollection<string> errors)
        {
            var node = topology[member];
            if (node is null)
            {
                return null;
            }

            if (node is JsonArray array)
            {
                return array;
            }

            errors.Add($"{member} must be an array");
            return null;
        }

        private static void CheckMembers(JsonObject node, IReadOnlyCollection<string> declared, string? where, ICollection<string> errors)
        {
            foreach (var member in node.Select(pair => pair.Key).Where(key => !declared.Contains(key, StringComparer.Ordinal)))
            {
                var prefix = where is null ? "" : $"{where}: ";
                errors.Add($"{prefix}'{member}' is not a topology field (parsing is strict; expected one of {string.Join(", ", declared)})");
            }
        }

        // A member of the wrong JSON kind is a malformed file, not a crash: JsonNode.GetValue<string>()
        // throws on a number or an object, and a validator that throws on a broken file reports nothing
        // about the rest of it.
        private static string? AsString(JsonNode? node)
        {
            return node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
        }
    }
}