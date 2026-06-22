using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Contracts.Conventions;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Configuration.Contract;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     A name path resolved against the wired host: which service config carries the member, plus an
    ///     optional trailing <see cref="FieldPath" /> that descends into a struct-typed member's scalar
    ///     field leaf (e.g. <c>RefControllableConsumer.AllocatedCurrent.L1</c>). The field segments are C#
    ///     member names (PascalCase), like the rest of the path; the schema's <c>properties</c> keys are
    ///     camelCase, matched by lower-casing the first char.
    /// </summary>
    internal sealed record ResolvedProperty(
        string Block,
        string ServiceIdentifier,
        string ServiceConfigId,
        string PropertyName,
        bool IsMeasuringPoint,
        IReadOnlyList<string>? FieldPath = null);

    /// <summary>
    ///     A contract reference resolved to its mocked endpoint (service provider / service / contract ids).
    ///     <see cref="HandlerName" /> is the generic stand-in's actor name (the contract's
    ///     <c>ContractHandlerActorName</c>) for the RFC 0010 <c>serviceProviderSet</c> drive; null for the
    ///     legacy HAL steps, which address their well-known handlers by value type.
    /// </summary>
    internal sealed record ResolvedContract(string ServiceProviderId, string ServiceId, string ContractId, string? HandlerName = null);

    /// <summary>
    ///     The resolved addressing for one step — exactly one of <see cref="Property" /> / <see cref="Contract" />
    ///     is set (wait / advance / settle steps have neither). For an <c>expect</c> with a relational
    ///     <c>{path}</c> comparand, <see cref="Comparand" /> additionally carries that path's resolution.
    /// </summary>
    internal sealed record ResolvedStep(ResolvedProperty? Property, ResolvedContract? Contract, ResolvedProperty? Comparand = null);

    /// <summary>
    ///     Resolves scenario name paths and contract references against <see cref="ConfigurationOutput" />
    ///     (RFC 0006 revision 5 name paths): <c>Block.Property</c> when the property is unambiguous within the
    ///     block, <c>Block.Service.Property</c> always. A two-segment path matching members on more than one
    ///     service is an error that lists the qualified candidates — never silent last-wins.
    /// </summary>
    internal sealed class ScenarioResolver
    {
        private readonly ConfigurationOutput _configuration;

        public ScenarioResolver(ConfigurationOutput configuration)
        {
            _configuration = configuration;
        }

        public ResolvedStep ResolveStep(ScenarioStep step, string where, List<string> errors)
        {
            switch (step.Kind)
            {
                case "set":
                {
                    var property = ResolveProperty(step.Set, where, errors);
                    if (property is not null)
                    {
                        if (property.IsMeasuringPoint)
                        {
                            errors.Add($"{where}: '{step.Set}' is a measuring point — read-only, not settable");
                        }
                        else if (IsReadOnly(property))
                        {
                            errors.Add($"{where}: '{step.Set}' is a read-only property");
                        }
                    }

                    return new ResolvedStep(property, null);
                }

                case "waitUntil":
                {
                    var property = ResolveProperty(step.WaitUntil!.Property, where, errors);
                    if (property is not null)
                    {
                        var numericComparator = step.WaitUntil.Above.ValueKind == JsonValueKind.Number || step.WaitUntil.Below.ValueKind == JsonValueKind.Number;
                        ValidateComparatorAgainstLeaf(property,
                                                      step.WaitUntil.Property,
                                                      numericComparator,
                                                      step.WaitUntil.OneOf,
                                                      where,
                                                      errors);
                    }

                    return new ResolvedStep(property, null);
                }

                case "expect":
                {
                    var property = ResolveProperty(step.Expect!.Property, where, errors);
                    var numericComparator = step.Expect.Above.ValueKind != JsonValueKind.Undefined || step.Expect.Below.ValueKind != JsonValueKind.Undefined;
                    if (property is not null)
                    {
                        ValidateComparatorAgainstLeaf(property,
                                                      step.Expect.Property,
                                                      numericComparator,
                                                      step.Expect.OneOf,
                                                      where,
                                                      errors);
                    }

                    // A relational {path} comparand resolves like the target; for above/below it must be a
                    // numeric leaf, matching the target-leaf rule.
                    var comparand = RelationalComparand(step.Expect);
                    ResolvedProperty? resolvedComparand = null;
                    if (comparand is { } comparandPath)
                    {
                        resolvedComparand = ResolveProperty(comparandPath, $"{where} comparand", errors);
                        if (resolvedComparand is not null && numericComparator)
                        {
                            var comparandType = EffectiveSchemaType(resolvedComparand);
                            if (comparandType != "number" && comparandType != "integer")
                            {
                                errors.Add($"{where}: above/below compare numbers, but the comparand '{comparandPath}' is of type '{comparandType ?? "unknown"}'");
                            }
                        }
                    }

                    return new ResolvedStep(property, null, resolvedComparand);
                }

                case "serviceProviderSet":
                    return new ResolvedStep(null, ResolveServiceProviderContract(step.ServiceProviderSet!.LogicBlock, step.ServiceProviderSet.Contract, true, where, errors));

                case "serviceProviderExpect":
                    return new ResolvedStep(null, ResolveServiceProviderContract(step.ServiceProviderExpect!.LogicBlock, step.ServiceProviderExpect.Contract, false, where, errors));

                default: // wait — nothing to resolve
                    return new ResolvedStep(null, null);
            }
        }

        public ResolvedProperty? ResolveProperty(string? path, string where, List<string> errors)
        {
            if (path is null || !TryParseSegments(path, out var segments))
            {
                errors.Add($"{where}: '{path}' is not a name path (Block.Property or Block.Service.Property, optionally followed by a struct field path)");
                return null;
            }

            var blockName = segments[0];
            var block = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == blockName);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{blockName}' in this topology" + Suggest(blockName, _configuration.LogicBlocks.Select(b => b.Name)));
                return null;
            }

            // Disambiguate Block.Service.Property (3+ seg) against Block.Property.Field (3+ seg) by the
            // CONFIG, never by counting segments: seg[1] is a service iff the block declares it. If seg[1]
            // is ALSO a valid member of the block, the path is genuinely ambiguous — list both readings.
            var service = segments.Count >= 3 ? block.Services.FirstOrDefault(s => s.Identifier == segments[1]) : null;
            var seg1IsMember = block.Services.Any(s => HasMember(s, segments[1]));

            if (service is not null && seg1IsMember)
            {
                var asService = $"{blockName}.{segments[1]}.{segments[2]} (service '{segments[1]}', member '{segments[2]}')";
                var asField = $"{blockName}.{segments[1]} (member '{segments[1]}') with field path '{string.Join(".", segments.Skip(2))}'";
                errors.Add($"{where}: '{path}' is ambiguous — '{segments[1]}' is both a service of '{blockName}' and a member; it could mean {asService} or {asField}");
                return null;
            }

            string propertyName;
            IReadOnlyList<string>? fieldPath;
            if (service is not null)
            {
                // Service-qualified: seg[2] = member, seg[3..] = field path.
                propertyName = segments[2];
                fieldPath = segments.Count > 3 ? segments.Skip(3).ToList() : null;
            }
            else
            {
                // seg[1] is an (unambiguous) member; seg[2..] = field path. The two-segment form falls out
                // of this with an empty field path.
                propertyName = segments[1];
                fieldPath = segments.Count > 2 ? segments.Skip(2).ToList() : null;

                // Two-segment member resolution must be unambiguous within the block (revision 5 rule).
                // seg[1] is known not to be a service here (the service branch above is taken otherwise),
                // so a zero-carrier miss means it is neither a service nor a member of the block.
                var carriers = block.Services.Where(s => HasMember(s, propertyName)).ToList();
                if (carriers.Count == 0)
                {
                    errors.Add($"{where}: block '{blockName}' has no property or measuring point '{propertyName}'" +
                               Suggest(propertyName, block.Services.Select(s => s.Identifier).Concat(block.Services.SelectMany(MemberNames))));
                    return null;
                }

                if (carriers.Count > 1)
                {
                    var candidates = string.Join(", ", carriers.Select(s => $"{blockName}.{s.Identifier}.{propertyName}"));
                    errors.Add($"{where}: '{path}' is ambiguous — '{propertyName}' exists on more than one service; qualify it: {candidates}");
                    return null;
                }

                service = carriers[0];
            }

            var resolved = Find(block,
                                service!,
                                propertyName,
                                where,
                                path,
                                errors);
            if (resolved is null)
            {
                return null;
            }

            if (fieldPath is null)
            {
                return resolved;
            }

            // Validate the field path against the member's schema before carrying it.
            if (!ValidateFieldPath(resolved, fieldPath, where, path, errors))
            {
                return null;
            }

            return resolved with { FieldPath = fieldPath };
        }

        // The comparator-vs-leaf check shared by waitUntil and expect, run against the RESOLVED LEAF (for a
        // struct field path the scalar field's schema type, not the object-typed member's): above/below need
        // a numeric leaf; equals/notEquals/oneOf reject a no-field struct/array target (else a notEquals
        // against a struct would instantly false-pass). oneOf elements are additionally type-checked here.
        private void ValidateComparatorAgainstLeaf(ResolvedProperty property,
                                                   string? path,
                                                   bool numericComparator,
                                                   JsonElement oneOf,
                                                   string where,
                                                   List<string> errors)
        {
            var type = EffectiveSchemaType(property);
            if (numericComparator && type != "number" && type != "integer")
            {
                errors.Add($"{where}: above/below compare numbers, but '{path}' is of type '{type ?? "unknown"}'");
            }

            if (!numericComparator && (type == "object" || type == "array"))
            {
                errors.Add($"{where}: '{path}' is a {type}-typed member — structs/arrays are not comparable in v1 (a scenario needing that is a C# test)");
            }

            // oneOf elements must fit the leaf type: a numeric leaf takes numbers; a string/enum leaf takes
            // strings; a boolean leaf takes booleans. (Element scalar-ness is enforced structurally.)
            if (oneOf.ValueKind == JsonValueKind.Array && type is "number" or "integer" or "string" or "boolean")
            {
                foreach (var element in oneOf.EnumerateArray())
                {
                    if (!ElementFitsLeaf(element.ValueKind, type))
                    {
                        errors.Add($"{where}: oneOf element {element.GetRawText()} does not fit '{path}' (type '{type}')");
                    }
                }
            }
        }

        private static bool ElementFitsLeaf(JsonValueKind element, string leafType)
        {
            return leafType switch
            {
                "number" or "integer" => element == JsonValueKind.Number,
                "string" => element == JsonValueKind.String,
                "boolean" => element is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
        }

        // The relational comparand path of an expect when the comparator value is a {path} object, else null.
        private static string? RelationalComparand(ScenarioExpect expect)
        {
            foreach (var comparand in new[] { expect.Above, expect.Below, expect.EqualTo, expect.NotEquals })
            {
                if (ScenarioComparators.IsPathComparand(comparand))
                {
                    return comparand.GetProperty("path").GetString();
                }
            }

            return null;
        }

        private static ResolvedProperty? Find(ConfigurationOutput.LogicBlock block,
                                              ConfigurationOutput.Service service,
                                              string propertyName,
                                              string where,
                                              string path,
                                              List<string> errors)
        {
            if (service.ServiceProperties.Any(p => p.Identifier == propertyName))
            {
                return new ResolvedProperty(block.Name, service.Identifier, service.Id, propertyName, false);
            }

            if (service.ServiceMeasuringPoints.Any(p => p.Identifier == propertyName))
            {
                return new ResolvedProperty(block.Name, service.Identifier, service.Id, propertyName, true);
            }

            errors.Add($"{where}: '{path}' does not resolve — service '{service.Identifier}' has no member '{propertyName}'" + Suggest(propertyName, MemberNames(service)));
            return null;
        }

        // Walks the field path against the member's JSON schema: each segment must exist in the current
        // object's "properties" map (camelCase keys; the path is PascalCase, matched by lowering the first
        // char), every intermediate must be "type":"object", and the leaf must be a scalar. Returns false
        // and records a helpful error (canonical PascalCase suggestion) on the first miss.
        private bool ValidateFieldPath(ResolvedProperty member, IReadOnlyList<string> fieldPath, string where, string path, List<string> errors)
        {
            var current = SchemaOf(member);
            for (var i = 0; i < fieldPath.Count; i++)
            {
                var segment = fieldPath[i];
                if (SchemaTypeOf(current) != "object")
                {
                    errors.Add($"{where}: '{path}' descends into '{segment}' but '{string.Join(".", new[] { member.PropertyName }.Concat(fieldPath.Take(i)))}' is not a struct");
                    return false;
                }

                var properties = current?["properties"] as JsonObject;
                var key = ToCamelCase(segment);
                var field = properties?[key];
                if (field is null)
                {
                    var available = properties?.Select(kvp => ToPascalCase(kvp.Key)) ?? Enumerable.Empty<string>();
                    errors.Add($"{where}: struct '{member.PropertyName}' has no field '{segment}'" + Suggest(segment, available));
                    return false;
                }

                current = field;
            }

            // Leaf must be a scalar — not an (object) nested struct, not an (array).
            var leafType = SchemaTypeOf(current);
            if (leafType == "object" || leafType == "array")
            {
                errors.Add($"{where}: '{path}' resolves to a {leafType}-typed field — only a scalar field leaf is addressable (structs/arrays are not comparable in v1)");
                return false;
            }

            return true;
        }

        // Resolves a generic service-provider value-contract reference (serviceProviderSet / serviceProviderExpect,
        // RFC 0010). Any [ServiceProviderContractType] contract on the block is addressable; direction is read off
        // the contract's Consumers multiplicity — ZeroOrOne (single-writer, surfaced as the Consumers annotation)
        // is an output (assert only); the omitted default ZeroOrMore is an input (drivable). The resolved
        // ContractHandlerActorName names the generic stand-in the drive addresses.
        private ResolvedContract? ResolveServiceProviderContract(string? blockName, string? contractId, bool forDrive, string where, List<string> errors)
        {
            var block = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == blockName);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{blockName}' in this topology" + Suggest(blockName!, _configuration.LogicBlocks.Select(b => b.Name)));
                return null;
            }

            var contract = block.Contracts.FirstOrDefault(c => c.Identifier == contractId);
            if (contract is null)
            {
                errors.Add($"{where}: block '{blockName}' has no contract '{contractId}'" + Suggest(contractId!, block.Contracts.Select(c => c.Identifier)));
                return null;
            }

            var isOutput = contract.Annotations.TryGetValue(LogicBlockWiringConventions.ConsumersAnnotationKey, out var consumers) &&
                           consumers as string == LogicBlockWiringConventions.ZeroOrOne;
            if (forDrive && isOutput)
            {
                errors.Add($"{where}: contract '{contractId}' is an output (single-writer) — assert it with serviceProviderExpect; it cannot be driven with serviceProviderSet");
                return null;
            }

            if (!forDrive && !isOutput)
            {
                errors.Add($"{where}: contract '{contractId}' is an input — drive it with serviceProviderSet; only an output (single-writer) is assertable with serviceProviderExpect");
                return null;
            }

            var mapping = block.ContractMappings.FirstOrDefault(m => m.ContractIdentifier == contractId);
            if (mapping is null)
            {
                errors.Add($"{where}: contract '{contractId}' has no mocked endpoint mapping in this topology");
                return null;
            }

            var handlerName = contract.Annotations.TryGetValue(ServiceProviderContractAnnotations.ContractHandlerActorName, out var handler) ? handler as string : null;

            return new ResolvedContract(mapping.MappedServiceProviderIdentifier, mapping.MappedServiceIdentifier, mapping.MappedContractIdentifier, handlerName);
        }

        private bool IsReadOnly(ResolvedProperty property)
        {
            var schema = SchemaOf(property);
            return schema?["readOnly"]?.GetValue<bool>() == true;
        }

        // The schema type at the RESOLVED LEAF: the member's own type for a no-field target, or the
        // scalar field's type when a field path is present (descending the "properties" maps). The
        // comparator check (above/below need a numeric leaf; equals/notEquals reject object/array) runs
        // against this.
        private string? EffectiveSchemaType(ResolvedProperty property)
        {
            var schema = SchemaOf(property);
            if (property.FieldPath is not null)
            {
                schema = DescendToLeaf(schema, property.FieldPath);
            }

            return SchemaTypeOf(schema);
        }

        // Reads a schema's "type", normalizing the ["object","null"] / ["number","null"] nullable-widened
        // form to its non-null member.
        private static string? SchemaTypeOf(JsonNode? schema)
        {
            var type = schema?["type"];
            return type switch
            {
                JsonValue value => value.GetValue<string>(),
                JsonArray array => array.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t != "null"),
                _ => null,
            };
        }

        // Walks the "properties" maps for the field path; returns the leaf field's schema, or null if any
        // segment is missing (the validation pass already guarantees a valid path before this runs).
        private static JsonNode? DescendToLeaf(JsonNode? schema, IReadOnlyList<string> fieldPath)
        {
            var current = schema;
            foreach (var segment in fieldPath)
            {
                current = (current?["properties"] as JsonObject)?[ToCamelCase(segment)];
                if (current is null)
                {
                    return null;
                }
            }

            return current;
        }

        private JsonNode? SchemaOf(ResolvedProperty property)
        {
            var service = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == property.Block)?.Services.FirstOrDefault(s => s.Identifier == property.ServiceIdentifier);
            return property.IsMeasuringPoint ? service?.ServiceMeasuringPoints.FirstOrDefault(p => p.Identifier == property.PropertyName)?.Schema :
                       service?.ServiceProperties.FirstOrDefault(p => p.Identifier == property.PropertyName)?.Schema;
        }

        private static IEnumerable<string> MemberNames(ConfigurationOutput.Service service)
        {
            return service.ServiceProperties.Select(p => p.Identifier).Concat(service.ServiceMeasuringPoints.Select(p => p.Identifier));
        }

        private static bool HasMember(ConfigurationOutput.Service service, string propertyName)
        {
            return service.ServiceProperties.Any(p => p.Identifier == propertyName) || service.ServiceMeasuringPoints.Any(p => p.Identifier == propertyName);
        }

        // Splits a name path into its dot-separated segments. A path needs at least two segments
        // (Block.Member); the interpretation of the tail (service-vs-member, plus any field path) is the
        // caller's, resolved against the config rather than by counting segments.
        private static bool TryParseSegments(string path, out IReadOnlyList<string> segments)
        {
            var parts = path.Split('.');
            if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
            {
                segments = Array.Empty<string>();
                return false;
            }

            segments = parts;
            return true;
        }

        // Lower the first char so a PascalCase path segment (e.g. "L1") matches the camelCase schema
        // "properties" key (the serializer's policy; see TypeRefBuilder.ToCamelCase).
        private static string ToCamelCase(string s)
        {
            return string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        // The inverse, for "did you mean" suggestions: report the canonical PascalCase the author writes.
        private static string ToPascalCase(string s)
        {
            return string.IsNullOrEmpty(s) || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // "did you mean" — case-insensitive match only; renames are the dominant failure and usually
        // differ in case or a suffix, and anything fancier belongs in `dale scenario validate`.
        private static string Suggest(string wanted, IEnumerable<string> available)
        {
            var match = available.FirstOrDefault(a => string.Equals(a, wanted, StringComparison.OrdinalIgnoreCase));
            return match is null ? string.Empty : $" (did you mean '{match}'? names are case-sensitive)";
        }
    }

    /// <summary>
    ///     The comparator semantics (RFC 0006 table) shared by <c>waitUntil</c> and <c>expect</c>, evaluated
    ///     against live CLR values from the event stream / value cache: <c>above</c>/<c>below</c> numeric;
    ///     <c>equals</c>/<c>notEquals</c> exact — numbers (optional tolerance), booleans, strings, enums by
    ///     case-sensitive member name, <c>null</c>; <c>oneOf</c> tests set membership. For <c>expect</c> a
    ///     relational comparand may be a <c>{path}</c> object — the runner resolves it and passes the leaf as
    ///     <c>comparandLive</c>, which then replaces the (absent) literal in the comparison.
    /// </summary>
    internal static class ScenarioConditions
    {
        public static bool IsSatisfied(ScenarioWaitUntil condition, object? live)
        {
            return Evaluate(condition.Above,
                            condition.Below,
                            condition.EqualTo,
                            condition.NotEquals,
                            condition.OneOf,
                            condition.Tolerance,
                            live,
                            null,
                            false);
        }

        // The serviceProviderExpect assert (RFC 0010) — identical comparator semantics, against the value the
        // block last wrote on a service-provider output contract (literals only).
        public static bool IsSatisfied(ScenarioServiceProviderAssert condition, object? live)
        {
            return Evaluate(condition.Above,
                            condition.Below,
                            condition.EqualTo,
                            condition.NotEquals,
                            condition.OneOf,
                            condition.Tolerance,
                            live,
                            null,
                            false);
        }

        // expect's evaluator — identical comparator logic, but a relational comparand resolved from a {path}
        // object is supplied via comparandLive when hasComparand is true; it replaces the literal in the
        // numeric/exact comparison. hasComparand (not comparandLive != null) is the signal, so a comparand
        // path that resolves to null is still handled as a relational comparison against null.
        public static bool IsSatisfied(ScenarioExpect condition, object? live, bool hasComparand, object? comparandLive)
        {
            return Evaluate(condition.Above,
                            condition.Below,
                            condition.EqualTo,
                            condition.NotEquals,
                            condition.OneOf,
                            condition.Tolerance,
                            live,
                            comparandLive,
                            hasComparand);
        }

        private static bool Evaluate(JsonElement above,
                                     JsonElement below,
                                     JsonElement equalTo,
                                     JsonElement notEquals,
                                     JsonElement oneOf,
                                     double? tolerance,
                                     object? live,
                                     object? comparandLive,
                                     bool hasComparand)
        {
            if (above.ValueKind != JsonValueKind.Undefined)
            {
                return TryAsDouble(live, out var value) && TryComparand(above, comparandLive, hasComparand, out var bound) && value > bound;
            }

            if (below.ValueKind != JsonValueKind.Undefined)
            {
                return TryAsDouble(live, out var value) && TryComparand(below, comparandLive, hasComparand, out var bound) && value < bound;
            }

            if (equalTo.ValueKind != JsonValueKind.Undefined)
            {
                return hasComparand ? AreEqualLive(comparandLive, live, tolerance) : AreEqual(equalTo, live, tolerance);
            }

            if (notEquals.ValueKind != JsonValueKind.Undefined)
            {
                return hasComparand ? !AreEqualLive(comparandLive, live, tolerance) : !AreEqual(notEquals, live, tolerance);
            }

            // oneOf: satisfied if live equals any element (each compared with the same exact semantics).
            return oneOf.ValueKind == JsonValueKind.Array && oneOf.EnumerateArray().Any(element => AreEqual(element, live, null));
        }

        // The numeric bound for above/below: the literal, or the resolved {path} comparand when present. A
        // comparand that does not read as a number is a no-match (the structural numeric-leaf check guards the
        // common case; this stays robust if a path resolves to null at run time).
        private static bool TryComparand(JsonElement literal, object? comparandLive, bool hasComparand, out double bound)
        {
            if (hasComparand)
            {
                return TryAsDouble(comparandLive, out bound);
            }

            bound = literal.GetDouble();
            return true;
        }

        // Equality between two LIVE CLR values (the {path} relational comparand path) — mirrors AreEqual's
        // numbers/booleans/strings/enums/null rules without a JsonElement on the expected side.
        private static bool AreEqualLive(object? expected, object? live, double? tolerance)
        {
            if (expected is null)
            {
                return live is null;
            }

            if (TryAsDouble(expected, out var expectedNumber))
            {
                return TryAsDouble(live, out var liveNumber) && (tolerance is null ? liveNumber.Equals(expectedNumber) : Math.Abs(liveNumber - expectedNumber) <= tolerance.Value);
            }

            if (expected is bool expectedBool)
            {
                return live is bool liveBool && liveBool == expectedBool;
            }

            return string.Equals(LiveText(expected), LiveText(live), StringComparison.Ordinal) && live is not null;
        }

        // The string projection used for exact string/enum/TimeSpan comparison.
        private static string? LiveText(object? live)
        {
            return live switch
            {
                null => null,
                string s => s,
                TimeSpan ts => ts.ToString(),
                _ when live.GetType().IsEnum => live.ToString(),
                _ => null,
            };
        }

        private static bool AreEqual(JsonElement expected, object? live, double? tolerance)
        {
            switch (expected.ValueKind)
            {
                case JsonValueKind.Null:
                    return live is null;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return live is bool b && b == (expected.ValueKind == JsonValueKind.True);

                case JsonValueKind.Number:
                    if (!TryAsDouble(live, out var value))
                    {
                        return false;
                    }

                    var target = expected.GetDouble();
                    return tolerance is null ? value.Equals(target) : Math.Abs(value - target) <= tolerance.Value;

                case JsonValueKind.String:
                    // Strings exact; enums by case-sensitive member name; TimeSpan via its .NET ToString
                    // form (durations stay edge-of-vocabulary in v1 — prefer above/below on numerics).
                    var liveText = LiveText(live);
                    return liveText is not null && string.Equals(liveText, expected.GetString(), StringComparison.Ordinal);

                default:
                    return false;
            }
        }

        private static bool TryAsDouble(object? live, out double value)
        {
            switch (live)
            {
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    value = Convert.ToDouble(live, CultureInfo.InvariantCulture);
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}