using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vion.Dale.Cli.Commands
{
    /// <summary>Outcome of validating one scenario file.</summary>
    public sealed class ScenarioCheckOutcome
    {
        /// <summary>Set when the file targets a different topology than the configuration — paths were not checked.</summary>
        public string? SkippedForTopology { get; init; }

        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    ///     The language-neutral validation core behind <c>dale scenario validate</c>: a deliberately lite
    ///     mirror of the RFC 0006 format rules and revision 5 name-path resolution, evaluated against the
    ///     wired-host configuration JSON (<c>dale dev --export-config</c> / <c>GET /api/configuration</c>).
    ///     The C# ScenarioRunner stays authoritative — this exists so CI and editors catch renames and
    ///     ambiguity without booting a host per file.
    /// </summary>
    public static class ScenarioFileChecks
    {
        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        public static ScenarioCheckOutcome Validate(string fileName, string json, JsonNode config)
        {
            var errors = new List<string>();
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException e)
            {
                return new ScenarioCheckOutcome { Errors = new[] { $"not valid JSON: {e.Message}" } };
            }

            if (root is not JsonObject scenario)
            {
                return new ScenarioCheckOutcome { Errors = new[] { "not a JSON object" } };
            }

            if (scenario["version"]?.GetValueKind() != JsonValueKind.Number || scenario["version"]!.GetValue<int>() != 1)
            {
                errors.Add("version must be 1");
            }

            var id = scenario["id"]?.GetValue<string>();
            var expectedId = fileName.EndsWith(".scenario.json", StringComparison.OrdinalIgnoreCase) ? fileName[..^".scenario.json".Length] : fileName;
            if (id is null || !IdSlug.IsMatch(id) || id.Contains("..") || string.Equals(id, "schema", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("id is required and must be a URL-safe slug (and not the reserved 'schema')");
            }
            else if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                errors.Add($"id '{id}' does not match the file name (expected '{expectedId}')");
            }

            var topology = scenario["topology"]?.GetValue<string>();
            if (string.IsNullOrEmpty(topology))
            {
                errors.Add("topology is required");
            }

            // A scenario for ANOTHER topology is legitimate (consumers keep one scenarios dir for many
            // presets; CI skips non-matching files) — structural errors above still count, but name
            // paths can only be resolved against the topology the configuration describes.
            var configTopology = config["topologyName"]?.GetValue<string>();
            var skipResolution = topology is not null && !string.Equals(topology, configTopology, StringComparison.Ordinal);

            ValidateSteps(scenario["setup"], "setup", true, skipResolution ? null : config, errors);
            ValidateSteps(scenario["steps"], "steps", false, skipResolution ? null : config, errors);

            if (scenario["watch"] is JsonArray watch)
            {
                for (var i = 0; i < watch.Count; i++)
                {
                    var path = watch[i]?.GetValue<string>();
                    if (path is null)
                    {
                        errors.Add($"watch[{i}]: not a string");
                    }
                    else if (!skipResolution)
                    {
                        ResolvePath(path, config, $"watch[{i}]", false, errors);
                    }
                }
            }

            if (scenario["judge"] is JsonArray judge)
            {
                for (var i = 0; i < judge.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(judge[i]?["text"]?.GetValue<string>()))
                    {
                        errors.Add($"judge[{i}]: text is required");
                    }
                }
            }

            return new ScenarioCheckOutcome
                   {
                       SkippedForTopology = skipResolution && errors.Count == 0 ? topology : null,
                       Errors = errors,
                   };
        }

        /// <summary>
        ///     Enrich the generic schema's name-path definition with an enum of every valid path in this
        ///     topology — completion and red squiggles in any editor, the type-safety substitute for the
        ///     rejected C# builder (RFC 0006). Two-segment forms are listed only when unambiguous.
        /// </summary>
        public static void EnrichSchemaWithNamePaths(JsonNode schemaDocument, JsonNode config)
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var block in config["logicBlocks"] as JsonArray ?? new JsonArray())
            {
                var blockName = block?["name"]?.GetValue<string>();
                if (blockName is null)
                {
                    continue;
                }

                var memberCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var services = block!["services"] as JsonArray ?? new JsonArray();
                foreach (var service in services)
                {
                    // Count DISTINCT carrier services — the resolver's ambiguity rule (ResolvePath's
                    // `carriers`). A member exposed as BOTH a serviceProperty and a serviceMeasuringPoint on
                    // the SAME service is one carrier, not two, so the two-segment form stays valid for a
                    // single-service block; Distinct() dedupes that prop+MP pair (DF-06).
                    foreach (var member in MemberNames(service).Distinct())
                    {
                        memberCounts[member] = memberCounts.TryGetValue(member, out var n) ? n + 1 : 1;
                    }
                }

                foreach (var service in services)
                {
                    var serviceIdentifier = service?["identifier"]?.GetValue<string>();
                    foreach (var member in MemberNames(service))
                    {
                        if (memberCounts[member] == 1)
                        {
                            paths.Add($"{blockName}.{member}");
                        }

                        if (serviceIdentifier is not null)
                        {
                            paths.Add($"{blockName}.{serviceIdentifier}.{member}");
                        }
                    }
                }
            }

            if (schemaDocument["$defs"]?["namePath"] is JsonObject namePath)
            {
                namePath["enum"] = new JsonArray(paths.Select(p => (JsonNode)p).ToArray());
                namePath.Remove("pattern");
            }
        }

        private static void ValidateSteps(JsonNode? section, string sectionName, bool setupOnlyShapes, JsonNode? config, List<string> errors)
        {
            if (section is null)
            {
                return;
            }

            if (section is not JsonArray steps)
            {
                errors.Add($"{sectionName} must be an array");
                return;
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var where = $"{sectionName}[{i}]";
                if (steps[i] is not JsonObject step)
                {
                    errors.Add($"{where}: not an object");
                    continue;
                }

                var shapes = new[] { "set", "digitalInput", "analogInput", "waitUntil", "wait", "advance", "settle" }.Count(k => step.ContainsKey(k));
                if (shapes != 1)
                {
                    errors.Add($"{where}: a step is exactly one of set / digitalInput / analogInput / waitUntil / wait / advance / settle");
                    continue;
                }

                if (setupOnlyShapes && (step.ContainsKey("waitUntil") || step.ContainsKey("wait") || step.ContainsKey("advance") || step.ContainsKey("settle")))
                {
                    errors.Add($"{where}: setup entries stage state — waits and time steps belong in steps");
                    continue;
                }

                if (step.ContainsKey("set"))
                {
                    if (!step.ContainsKey("value"))
                    {
                        errors.Add($"{where}: set requires value (use an explicit null to write null)");
                    }

                    var path = step["set"]?.GetValue<string>();
                    if (path is not null && config is not null)
                    {
                        ResolvePath(path, config, where, true, errors);
                    }
                }
                else if (step.ContainsKey("digitalInput") || step.ContainsKey("analogInput"))
                {
                    var digital = step.ContainsKey("digitalInput");
                    var kind = digital ? "digitalInput" : "analogInput";
                    var expectedValueKind = digital ? "boolean" : "number";
                    var valueKind = step["value"]?.GetValueKind();
                    var valueOk = digital ? valueKind is JsonValueKind.True or JsonValueKind.False : valueKind == JsonValueKind.Number;
                    if (!valueOk)
                    {
                        errors.Add($"{where}: {kind} requires a {expectedValueKind} value");
                    }

                    if (config is not null)
                    {
                        ResolveContract(step[kind], digital ? "DigitalInput" : "AnalogInput", config, where, errors);
                    }
                }
                else if (step.ContainsKey("waitUntil"))
                {
                    ValidateWaitUntil(step, config, where, errors);
                }
                else if (step.ContainsKey("advance"))
                {
                    if (step["advance"]?["seconds"]?.GetValueKind() != JsonValueKind.Number || step["advance"]!["seconds"]!.GetValue<double>() <= 0)
                    {
                        errors.Add($"{where}: advance.seconds must be a positive number");
                    }
                }
                else if (step.ContainsKey("settle"))
                {
                    // settle may be an empty object {}; maxSeconds is optional but must be positive when present.
                    if (step["settle"] is JsonObject settle && settle.ContainsKey("maxSeconds"))
                    {
                        if (settle["maxSeconds"]?.GetValueKind() != JsonValueKind.Number || settle["maxSeconds"]!.GetValue<double>() <= 0)
                        {
                            errors.Add($"{where}: settle.maxSeconds must be a positive number");
                        }
                    }
                }
                else if (step["wait"]?["seconds"]?.GetValueKind() != JsonValueKind.Number || step["wait"]!["seconds"]!.GetValue<double>() <= 0)
                {
                    errors.Add($"{where}: wait.seconds must be a positive number");
                }
            }
        }

        private static void ValidateWaitUntil(JsonObject step, JsonNode? config, string where, List<string> errors)
        {
            if (step["waitUntil"] is not JsonObject waitUntil)
            {
                errors.Add($"{where}: waitUntil must be an object");
                return;
            }

            var comparators = new[] { "above", "below", "equals", "notEquals" }.Where(waitUntil.ContainsKey).ToList();
            if (comparators.Count != 1)
            {
                errors.Add($"{where}: waitUntil takes exactly one of above / below / equals / notEquals");
            }

            foreach (var numeric in new[] { "above", "below" }.Where(waitUntil.ContainsKey))
            {
                if (waitUntil[numeric]?.GetValueKind() != JsonValueKind.Number)
                {
                    errors.Add($"{where}: waitUntil.{numeric} must be a number");
                }
            }

            foreach (var exact in new[] { "equals", "notEquals" }.Where(waitUntil.ContainsKey))
            {
                if (waitUntil[exact]?.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array)
                {
                    errors.Add($"{where}: waitUntil.{exact} does not compare structs/arrays in v1");
                }
            }

            var path = waitUntil["property"]?.GetValue<string>();
            if (path is null)
            {
                errors.Add($"{where}: waitUntil.property is required");
            }
            else if (config is not null)
            {
                ResolvePath(path, config, where, false, errors);
            }

            if (step.TryGetPropertyValue("timeoutSeconds", out var timeout) && (timeout?.GetValueKind() != JsonValueKind.Number || timeout.GetValue<double>() <= 0))
            {
                errors.Add($"{where}: timeoutSeconds must be a positive number");
            }
        }

        // Revision 5 name-path resolution against the configuration JSON — the same rules the runner
        // applies: two segments need a unique carrier (ambiguity lists the qualified candidates), three
        // segments always work, set targets must be writable service properties.
        private static void ResolvePath(string path, JsonNode config, string where, bool forWrite, List<string> errors)
        {
            var segments = path.Split('.');
            if (segments.Length is < 2 or > 3 || segments.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{where}: '{path}' is not a name path (Block.Property or Block.Service.Property)");
                return;
            }

            var block = (config["logicBlocks"] as JsonArray ?? new JsonArray()).FirstOrDefault(b => b?["name"]?.GetValue<string>() == segments[0]);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{segments[0]}' in this topology");
                return;
            }

            var services = block["services"] as JsonArray ?? new JsonArray();
            var property = segments[^1];
            JsonNode? service;
            if (segments.Length == 3)
            {
                service = services.FirstOrDefault(s => s?["identifier"]?.GetValue<string>() == segments[1]);
                if (service is null || !MemberNames(service).Contains(property))
                {
                    errors.Add($"{where}: '{path}' does not resolve (no service '{segments[1]}' with member '{property}')");
                    return;
                }
            }
            else
            {
                var carriers = services.Where(s => MemberNames(s).Contains(property)).ToList();
                if (carriers.Count == 0)
                {
                    errors.Add($"{where}: block '{segments[0]}' has no property or measuring point '{property}'");
                    return;
                }

                if (carriers.Count > 1)
                {
                    var candidates = string.Join(", ", carriers.Select(s => $"{segments[0]}.{s?["identifier"]?.GetValue<string>()}.{property}"));
                    errors.Add($"{where}: '{path}' is ambiguous — qualify it: {candidates}");
                    return;
                }

                service = carriers[0];
            }

            if (forWrite)
            {
                var propertyNode = (service?["serviceProperties"] as JsonArray ?? new JsonArray()).FirstOrDefault(p => p?["identifier"]?.GetValue<string>() == property);
                if (propertyNode is null)
                {
                    errors.Add($"{where}: '{path}' is a measuring point — read-only, not settable");
                }
                else if (propertyNode["schema"]?["readOnly"]?.GetValue<bool>() == true)
                {
                    errors.Add($"{where}: '{path}' is a read-only property");
                }
            }
        }

        private static void ResolveContract(JsonNode? reference, string expectedType, JsonNode config, string where, List<string> errors)
        {
            var blockName = reference?["block"]?.GetValue<string>();
            var contractId = reference?["contract"]?.GetValue<string>();
            if (blockName is null || contractId is null)
            {
                errors.Add($"{where}: block and contract are required");
                return;
            }

            var block = (config["logicBlocks"] as JsonArray ?? new JsonArray()).FirstOrDefault(b => b?["name"]?.GetValue<string>() == blockName);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{blockName}' in this topology");
                return;
            }

            var contract = (block["contracts"] as JsonArray ?? new JsonArray()).FirstOrDefault(c => c?["identifier"]?.GetValue<string>() == contractId);
            if (contract is null)
            {
                errors.Add($"{where}: block '{blockName}' has no contract '{contractId}'");
                return;
            }

            if (contract["matchingContractType"]?.GetValue<string>() != expectedType)
            {
                errors.Add($"{where}: contract '{contractId}' is a {contract["matchingContractType"]}, not a {expectedType}");
            }
        }

        private static IEnumerable<string> MemberNames(JsonNode? service)
        {
            foreach (var property in service?["serviceProperties"] as JsonArray ?? new JsonArray())
            {
                if (property?["identifier"]?.GetValue<string>() is { } name)
                {
                    yield return name;
                }
            }

            foreach (var measuringPoint in service?["serviceMeasuringPoints"] as JsonArray ?? new JsonArray())
            {
                if (measuringPoint?["identifier"]?.GetValue<string>() is { } name)
                {
                    yield return name;
                }
            }
        }
    }
}