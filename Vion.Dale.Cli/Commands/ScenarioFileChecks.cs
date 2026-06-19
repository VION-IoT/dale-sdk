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
        ///     Struct-typed members are expanded: for each scalar field leaf (possibly nested) a
        ///     <c>Block.Member.Field</c> (or <c>Block.Service.Member.Field</c>) path is also emitted.
        ///     Field segment keys are PascalCase (the schema <c>properties</c> keys are camelCase; the first
        ///     char is upper-cased, matching the path convention used by the resolver and runner).
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
                    foreach (var (member, memberSchema) in MemberNamesWithSchema(service))
                    {
                        // Always emit the member path itself (scalar leaf or struct — set accepts a whole struct).
                        if (memberCounts[member] == 1)
                        {
                            paths.Add($"{blockName}.{member}");
                        }

                        if (serviceIdentifier is not null)
                        {
                            paths.Add($"{blockName}.{serviceIdentifier}.{member}");
                        }

                        // If the member's schema is a struct (type:object with properties), also emit every
                        // scalar leaf path so editors autocomplete Block.Member.Field paths.
                        if (memberSchema is not null && IsStructSchema(memberSchema))
                        {
                            foreach (var fieldSuffix in StructFieldPaths(memberSchema))
                            {
                                if (memberCounts[member] == 1)
                                {
                                    paths.Add($"{blockName}.{member}.{fieldSuffix}");
                                }

                                if (serviceIdentifier is not null)
                                {
                                    paths.Add($"{blockName}.{serviceIdentifier}.{member}.{fieldSuffix}");
                                }
                            }
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

        // Returns true when a JSON Schema node represents a struct (type:object with a properties map).
        // Handles both the plain form { "type": "object" } and the nullable-widened form
        // { "type": ["object", "null"] } that the generator emits for nullable structs.
        private static bool IsStructSchema(JsonNode schema)
        {
            var type = schema["type"];
            bool isObject;
            if (type is JsonValue)
            {
                isObject = type.GetValue<string>() == "object";
            }
            else if (type is JsonArray typeArray)
            {
                isObject = typeArray.Any(t => t?.GetValue<string>() == "object");
            }
            else
            {
                isObject = false;
            }

            return isObject && schema["properties"] is JsonObject;
        }

        // Recursively walks the "properties" map of a struct schema and yields dot-joined PascalCase field
        // paths for every scalar leaf. Nested structs are descended; non-scalar leaf nodes (type:object,
        // type:array) are skipped — only addressable scalar leaves are emitted.
        private static IEnumerable<string> StructFieldPaths(JsonNode structSchema)
        {
            var properties = structSchema["properties"] as JsonObject;
            if (properties is null)
            {
                yield break;
            }

            foreach (var kvp in properties)
            {
                var fieldSchema = kvp.Value;
                if (fieldSchema is null)
                {
                    continue;
                }

                // Convert the camelCase property key to PascalCase (upper-case the first char).
                var fieldName = string.IsNullOrEmpty(kvp.Key) ? kvp.Key : char.ToUpperInvariant(kvp.Key[0]) + kvp.Key.Substring(1);

                if (IsStructSchema(fieldSchema))
                {
                    // Nested struct — recurse and prepend this segment.
                    foreach (var subPath in StructFieldPaths(fieldSchema))
                    {
                        yield return $"{fieldName}.{subPath}";
                    }
                }
                else
                {
                    // Scalar (or array, which is not addressable) — emit only non-array scalars.
                    var type = fieldSchema["type"];
                    string? typeString = null;
                    if (type is JsonValue tv)
                    {
                        typeString = tv.GetValue<string>();
                    }
                    else if (type is JsonArray ta)
                    {
                        typeString = ta.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t != "null");
                    }

                    if (typeString != "object" && typeString != "array")
                    {
                        yield return fieldName;
                    }
                }
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

                var shapes =
                    new[] { "set", "digitalInput", "analogInput", "digitalOutput", "analogOutput", "waitUntil", "expect", "wait", "advance", "settle" }
                        .Count(k => step.ContainsKey(k));
                if (shapes != 1)
                {
                    errors.Add($"{where}: a step is exactly one of set / digitalInput / analogInput / digitalOutput / analogOutput / waitUntil / expect / wait / advance / settle");
                    continue;
                }

                if (setupOnlyShapes && (step.ContainsKey("waitUntil") || step.ContainsKey("expect") || step.ContainsKey("digitalOutput") || step.ContainsKey("analogOutput") ||
                                        step.ContainsKey("wait") || step.ContainsKey("advance") || step.ContainsKey("settle")))
                {
                    errors.Add($"{where}: setup entries stage state — waits, expects, output asserts, and time steps belong in steps");
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
                else if (step.ContainsKey("digitalOutput") || step.ContainsKey("analogOutput"))
                {
                    // The read half of HAL: a contract-ref + comparator on a mocked output, resolved like the
                    // input drive steps but against an output contract type. Comparands are literals only
                    // (allowPathComparand: false) — the relational { path } form is expect-only.
                    var digital = step.ContainsKey("digitalOutput");
                    var kind = digital ? "digitalOutput" : "analogOutput";
                    if (step[kind] is JsonObject assert)
                    {
                        ValidateComparators(kind, assert, false, where, errors);
                        if (config is not null)
                        {
                            ResolveContract(assert, digital ? "DigitalOutput" : "AnalogOutput", config, where, errors);
                        }
                    }
                    else
                    {
                        errors.Add($"{where}: {kind} must be an object");
                    }
                }
                else if (step.ContainsKey("waitUntil"))
                {
                    ValidateWaitUntil(step, config, where, errors);
                }
                else if (step.ContainsKey("expect"))
                {
                    ValidateExpect(step, config, where, errors);
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
                    if (step["settle"] is JsonObject settle)
                    {
                        if (settle.ContainsKey("maxSeconds") && (settle["maxSeconds"]?.GetValueKind() != JsonValueKind.Number || settle["maxSeconds"]!.GetValue<double>() <= 0))
                        {
                            errors.Add($"{where}: settle.maxSeconds must be a positive number");
                        }

                        // settle.until, when present, scopes convergence to explicit target paths: a non-empty
                        // array of name paths that resolve against the topology (omit it to settle over watch).
                        if (settle.ContainsKey("until"))
                        {
                            if (settle["until"] is not JsonArray until || until.Count == 0)
                            {
                                errors.Add($"{where}: settle.until must be a non-empty array of name paths (omit it to settle over the whole watch list)");
                            }
                            else
                            {
                                for (var u = 0; u < until.Count; u++)
                                {
                                    var element = until[u];
                                    if (element is null || element.GetValueKind() != JsonValueKind.String)
                                    {
                                        errors.Add($"{where}: settle.until[{u}] not a string");
                                    }
                                    else if (string.IsNullOrWhiteSpace(element.GetValue<string>()))
                                    {
                                        // Structural, config-independent — mirror the model (ScenarioFile) so the
                                        // CLI and the runner agree even when path resolution is skipped.
                                        errors.Add($"{where}: settle.until[{u}]: empty name path");
                                    }
                                    else if (config is not null)
                                    {
                                        ResolvePath(element.GetValue<string>(), config, $"{where} settle.until[{u}]", false, errors);
                                    }
                                }
                            }
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

            ValidateComparators("waitUntil", waitUntil, false, where, errors);

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

        private static void ValidateExpect(JsonObject step, JsonNode? config, string where, List<string> errors)
        {
            if (step["expect"] is not JsonObject expect)
            {
                errors.Add($"{where}: expect must be an object");
                return;
            }

            ValidateComparators("expect", expect, true, where, errors);

            var path = expect["property"]?.GetValue<string>();
            if (path is null)
            {
                errors.Add($"{where}: expect.property is required");
            }
            else if (config is not null)
            {
                ResolvePath(path, config, where, false, errors);
            }

            // A relational {path} comparand must itself resolve (offline structural — the runner enforces the
            // numeric-leaf rule for above/below at run time).
            if (config is not null)
            {
                foreach (var name in new[] { "above", "below", "equals", "notEquals" })
                {
                    if (PathComparand(expect[name]) is { } comparandPath)
                    {
                        ResolvePath(comparandPath, config, $"{where} comparand", false, errors);
                    }
                }
            }
        }

        // The comparator block shared by waitUntil and expect: exactly one of above / below / equals /
        // notEquals / oneOf; above/below numeric (or a {path} object for expect); equals/notEquals reject
        // struct/array literals (the {path} object is the only allowed object form, expect only); oneOf is a
        // non-empty array of scalars.
        private static void ValidateComparators(string shape, JsonObject comparators, bool allowPathComparand, string where, List<string> errors)
        {
            var present = new[] { "above", "below", "equals", "notEquals", "oneOf" }.Where(comparators.ContainsKey).ToList();
            if (present.Count != 1)
            {
                errors.Add($"{where}: {shape} takes exactly one of above / below / equals / notEquals / oneOf");
            }

            foreach (var numeric in new[] { "above", "below" }.Where(comparators.ContainsKey))
            {
                if (comparators[numeric]?.GetValueKind() != JsonValueKind.Number && !(allowPathComparand && IsPathComparand(comparators[numeric])))
                {
                    errors.Add($"{where}: {shape}.{numeric} must be a number");
                }
            }

            foreach (var exact in new[] { "equals", "notEquals" }.Where(comparators.ContainsKey))
            {
                if (comparators[exact]?.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array && !(allowPathComparand && IsPathComparand(comparators[exact])))
                {
                    errors.Add($"{where}: {shape}.{exact} does not compare structs/arrays in v1");
                }
            }

            if (comparators.ContainsKey("oneOf"))
            {
                if (comparators["oneOf"] is not JsonArray oneOf || oneOf.Count == 0)
                {
                    errors.Add($"{where}: {shape}.oneOf must be a non-empty array of scalars");
                }
                else if (oneOf.Any(e => e?.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array))
                {
                    errors.Add($"{where}: {shape}.oneOf elements must be scalars (no objects/arrays)");
                }
            }

            // tolerance is only meaningful on a numeric equals — mirror the runner's structural rule
            // (ScenarioFile.StructuralErrors) so `dale scenario validate` and the schema fail as early as
            // the loader, instead of green-lighting e.g. `{ above, tolerance }` the run then rejects (DF-22).
            if (comparators.ContainsKey("tolerance"))
            {
                if (comparators["tolerance"]?.GetValueKind() == JsonValueKind.Number && comparators["tolerance"]!.GetValue<double>() < 0)
                {
                    errors.Add($"{where}: {shape}.tolerance must be non-negative");
                }

                // A literal number, or (expect only) a {path} comparand whose resolved value is checked numeric at run time.
                var equals = comparators.ContainsKey("equals") ? comparators["equals"] : null;
                if (!(equals?.GetValueKind() == JsonValueKind.Number || (allowPathComparand && IsPathComparand(equals))))
                {
                    errors.Add($"{where}: {shape}.tolerance is only valid with a numeric equals");
                }
            }
        }

        // The {path} string of a relational comparand object, or null when it is not the {path} form.
        private static string? PathComparand(JsonNode? comparand)
        {
            return IsPathComparand(comparand) ? comparand!["path"]!.GetValue<string>() : null;
        }

        private static bool IsPathComparand(JsonNode? comparand)
        {
            return comparand is JsonObject obj && obj.Count == 1 && obj["path"]?.GetValueKind() == JsonValueKind.String;
        }

        // Revision 5 name-path resolution against the configuration JSON — the same rules the runner
        // applies: two segments need a unique carrier (ambiguity lists the qualified candidates), three
        // segments always work, set targets must be writable service properties.
        private static void ResolvePath(string path, JsonNode config, string where, bool forWrite, List<string> errors)
        {
            var segments = path.Split('.');
            if (segments.Length < 2 || segments.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{where}: '{path}' is not a name path (Block.Property or Block.Service.Property, optionally followed by a struct field path)");
                return;
            }

            var block = (config["logicBlocks"] as JsonArray ?? new JsonArray()).FirstOrDefault(b => b?["name"]?.GetValue<string>() == segments[0]);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{segments[0]}' in this topology");
                return;
            }

            var services = block["services"] as JsonArray ?? new JsonArray();

            // Disambiguate Block.Service.Member(.Field) vs Block.Member.Field by the CONFIG, never by counting
            // segments (mirrors ScenarioResolver.ResolveProperty): seg[1] is a service iff the block declares it.
            // If seg[1] is ALSO a member, the path is genuinely ambiguous.
            var service = segments.Length >= 3 ? services.FirstOrDefault(s => s?["identifier"]?.GetValue<string>() == segments[1]) : null;
            var seg1IsMember = services.Any(s => MemberNames(s).Contains(segments[1]));

            if (service is not null && seg1IsMember)
            {
                errors.Add($"{where}: '{path}' is ambiguous — '{segments[1]}' is both a service of '{segments[0]}' and a member");
                return;
            }

            string property;
            string[] fieldPath;
            if (service is not null)
            {
                // Service-qualified: seg[2] = member, seg[3..] = struct field path.
                property = segments[2];
                fieldPath = segments.Skip(3).ToArray();
                if (!MemberNames(service).Contains(property))
                {
                    errors.Add($"{where}: '{path}' does not resolve — service '{segments[1]}' has no member '{property}'");
                    return;
                }
            }
            else
            {
                // seg[1] is an (unambiguous) member; seg[2..] = struct field path. The two-segment form falls
                // out of this with an empty field path.
                property = segments[1];
                fieldPath = segments.Skip(2).ToArray();
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

            // A trailing struct field path must descend the member's struct schema to a scalar leaf — mirroring
            // both EnrichSchemaWithNamePaths (which OFFERS these paths) and ScenarioResolver.ValidateFieldPath
            // (which RESOLVES them at run time). Without this branch, validate rejects a path its own schema
            // advertises and the runner accepts (DF-26).
            if (fieldPath.Length > 0)
            {
                var memberSchema = MemberNamesWithSchema(service).Where(m => m.Name == property).Select(m => m.Schema).FirstOrDefault();
                ValidateStructFieldPath(memberSchema,
                                        property,
                                        fieldPath,
                                        path,
                                        where,
                                        errors);
            }
        }

        // Walks a struct field path against the member's JSON schema (mirrors ScenarioResolver.ValidateFieldPath):
        // each intermediate must be a struct (type:object), each PascalCase segment maps to the camelCase
        // properties key, and the leaf must be a scalar (not object/array). Records the first miss.
        private static void ValidateStructFieldPath(JsonNode? memberSchema,
                                                    string member,
                                                    IReadOnlyList<string> fieldPath,
                                                    string path,
                                                    string where,
                                                    List<string> errors)
        {
            var current = memberSchema;
            for (var i = 0; i < fieldPath.Count; i++)
            {
                var segment = fieldPath[i];
                if (SchemaType(current) != "object")
                {
                    var prefix = string.Join(".", new[] { member }.Concat(fieldPath.Take(i)));
                    errors.Add($"{where}: '{path}' descends into '{segment}' but '{prefix}' is not a struct");
                    return;
                }

                var properties = current?["properties"] as JsonObject;
                var key = string.IsNullOrEmpty(segment) ? segment : char.ToLowerInvariant(segment[0]) + segment.Substring(1);
                var field = properties?[key];
                if (field is null)
                {
                    errors.Add($"{where}: struct '{member}' has no field '{segment}'");
                    return;
                }

                current = field;
            }

            var leafType = SchemaType(current);
            if (leafType is "object" or "array")
            {
                errors.Add($"{where}: '{path}' resolves to a {leafType}-typed field — only a scalar field leaf is addressable (structs/arrays are not comparable in v1)");
            }
        }

        // The JSON-Schema "type" of a node, picking the non-null member of a nullable-widened ["x", "null"] array.
        private static string? SchemaType(JsonNode? schema)
        {
            var type = schema?["type"];
            if (type is JsonValue value)
            {
                return value.GetValue<string>();
            }

            if (type is JsonArray array)
            {
                return array.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t != "null");
            }

            return null;
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
            foreach (var (name, _) in MemberNamesWithSchema(service))
            {
                yield return name;
            }
        }

        // Like MemberNames but also yields the member's "schema" node so the caller can inspect it for
        // struct expansion. Used by EnrichSchemaWithNamePaths to emit Block.Member.Field paths.
        private static IEnumerable<(string Name, JsonNode? Schema)> MemberNamesWithSchema(JsonNode? service)
        {
            foreach (var property in service?["serviceProperties"] as JsonArray ?? new JsonArray())
            {
                if (property?["identifier"]?.GetValue<string>() is { } name)
                {
                    yield return (name, property["schema"]);
                }
            }

            foreach (var measuringPoint in service?["serviceMeasuringPoints"] as JsonArray ?? new JsonArray())
            {
                if (measuringPoint?["identifier"]?.GetValue<string>() is { } name)
                {
                    yield return (name, measuringPoint["schema"]);
                }
            }
        }
    }
}