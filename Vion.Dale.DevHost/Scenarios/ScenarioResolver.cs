using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>A name path resolved against the wired host: which service config carries the member.</summary>
    internal sealed record ResolvedProperty(string Block, string ServiceIdentifier, string ServiceConfigId, string PropertyName, bool IsMeasuringPoint);

    /// <summary>A contract reference resolved to its mocked endpoint (service provider / service / contract ids).</summary>
    internal sealed record ResolvedContract(string ServiceProviderId, string ServiceId, string ContractId);

    /// <summary>
    ///     The resolved addressing for one step — exactly one of the two is set (waits have neither... wait steps have
    ///     neither).
    /// </summary>
    internal sealed record ResolvedStep(ResolvedProperty? Property, ResolvedContract? Contract);

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
                        var type = EffectiveSchemaType(property);
                        var numericComparator = step.WaitUntil.Above.ValueKind == JsonValueKind.Number || step.WaitUntil.Below.ValueKind == JsonValueKind.Number;
                        if (numericComparator && type != "number" && type != "integer")
                        {
                            errors.Add($"{where}: above/below compare numbers, but '{step.WaitUntil.Property}' is of type '{type ?? "unknown"}'");
                        }

                        // equals/notEquals never compare structs/arrays in v1 — reject the TARGET being
                        // composite too, not just the comparand (a notEquals against a struct would
                        // instantly false-pass otherwise).
                        if (!numericComparator && (type == "object" || type == "array"))
                        {
                            errors.Add($"{where}: '{step.WaitUntil.Property}' is a {type}-typed member — structs/arrays are not comparable in v1 (a scenario needing that is a C# test)");
                        }
                    }

                    return new ResolvedStep(property, null);
                }

                case "digitalInput":
                    return new ResolvedStep(null, ResolveContract(step.DigitalInput!, "DigitalInput", where, errors));

                case "analogInput":
                    return new ResolvedStep(null, ResolveContract(step.AnalogInput!, "AnalogInput", where, errors));

                default: // wait — nothing to resolve
                    return new ResolvedStep(null, null);
            }
        }

        public ResolvedProperty? ResolveProperty(string? path, string where, List<string> errors)
        {
            if (path is null || !TryParsePath(path, out var blockName, out var serviceIdentifier, out var propertyName))
            {
                errors.Add($"{where}: '{path}' is not a name path (Block.Property or Block.Service.Property)");
                return null;
            }

            var block = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == blockName);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{blockName}' in this topology" + Suggest(blockName, _configuration.LogicBlocks.Select(b => b.Name)));
                return null;
            }

            if (serviceIdentifier is not null)
            {
                var service = block.Services.FirstOrDefault(s => s.Identifier == serviceIdentifier);
                if (service is null)
                {
                    errors.Add($"{where}: block '{blockName}' has no service '{serviceIdentifier}'" + Suggest(serviceIdentifier, block.Services.Select(s => s.Identifier)));
                    return null;
                }

                return Find(block,
                            service,
                            propertyName,
                            where,
                            path,
                            errors);
            }

            // Two-segment form: the property must be unambiguous within the block.
            var carriers = block.Services.Where(s => HasMember(s, propertyName)).ToList();
            if (carriers.Count == 0)
            {
                errors.Add($"{where}: block '{blockName}' has no property or measuring point '{propertyName}'" + Suggest(propertyName, block.Services.SelectMany(MemberNames)));
                return null;
            }

            if (carriers.Count > 1)
            {
                var candidates = string.Join(", ", carriers.Select(s => $"{blockName}.{s.Identifier}.{propertyName}"));
                errors.Add($"{where}: '{path}' is ambiguous — '{propertyName}' exists on more than one service; qualify it: {candidates}");
                return null;
            }

            return Find(block,
                        carriers[0],
                        propertyName,
                        where,
                        path,
                        errors);
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

        private ResolvedContract? ResolveContract(ScenarioContractRef reference, string expectedType, string where, List<string> errors)
        {
            var block = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == reference.Block);
            if (block is null)
            {
                errors.Add($"{where}: no logic block named '{reference.Block}' in this topology" + Suggest(reference.Block!, _configuration.LogicBlocks.Select(b => b.Name)));
                return null;
            }

            var contract = block.Contracts.FirstOrDefault(c => c.Identifier == reference.Contract);
            if (contract is null)
            {
                errors.Add($"{where}: block '{reference.Block}' has no contract '{reference.Contract}'" + Suggest(reference.Contract!, block.Contracts.Select(c => c.Identifier)));
                return null;
            }

            if (contract.MatchingContractType != expectedType)
            {
                errors.Add($"{where}: contract '{reference.Contract}' is a {contract.MatchingContractType}, not a {expectedType}");
                return null;
            }

            var mapping = block.ContractMappings.FirstOrDefault(m => m.ContractIdentifier == reference.Contract);
            if (mapping is null)
            {
                errors.Add($"{where}: contract '{reference.Contract}' has no mocked endpoint mapping in this topology");
                return null;
            }

            return new ResolvedContract(mapping.MappedServiceProviderIdentifier, mapping.MappedServiceIdentifier, mapping.MappedContractIdentifier);
        }

        private bool IsReadOnly(ResolvedProperty property)
        {
            var schema = SchemaOf(property);
            return schema?["readOnly"]?.GetValue<bool>() == true;
        }

        private string? EffectiveSchemaType(ResolvedProperty property)
        {
            var type = SchemaOf(property)?["type"];
            return type switch
            {
                JsonValue value => value.GetValue<string>(),
                JsonArray array => array.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t != "null"),
                _ => null,
            };
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

        private static bool TryParsePath(string path, out string block, out string? service, out string property)
        {
            block = string.Empty;
            service = null;
            property = string.Empty;
            var segments = path.Split('.');
            if (segments.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            switch (segments.Length)
            {
                case 2:
                    block = segments[0];
                    property = segments[1];
                    return true;
                case 3:
                    block = segments[0];
                    service = segments[1];
                    property = segments[2];
                    return true;
                default:
                    return false;
            }
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
    ///     The <c>waitUntil</c> comparison semantics (RFC 0006 table), evaluated against live CLR values from
    ///     the event stream / value cache: <c>above</c>/<c>below</c> numeric; <c>equals</c>/<c>notEquals</c>
    ///     exact — numbers (optional tolerance), booleans, strings, enums by case-sensitive member name,
    ///     <c>null</c> awaits the property becoming null.
    /// </summary>
    internal static class ScenarioConditions
    {
        public static bool IsSatisfied(ScenarioWaitUntil condition, object? live)
        {
            if (condition.Above.ValueKind == JsonValueKind.Number)
            {
                return TryAsDouble(live, out var value) && value > condition.Above.GetDouble();
            }

            if (condition.Below.ValueKind == JsonValueKind.Number)
            {
                return TryAsDouble(live, out var value) && value < condition.Below.GetDouble();
            }

            if (condition.EqualTo.ValueKind != JsonValueKind.Undefined)
            {
                return AreEqual(condition.EqualTo, live, condition.Tolerance);
            }

            return !AreEqual(condition.NotEquals, live, condition.Tolerance);
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
                    if (live is null)
                    {
                        return false;
                    }

                    // Strings exact; enums by case-sensitive member name; TimeSpan via its .NET ToString
                    // form (durations stay edge-of-vocabulary in v1 — prefer above/below on numerics).
                    var liveText = live switch
                    {
                        string s => s,
                        TimeSpan ts => ts.ToString(),
                        _ when live.GetType().IsEnum => live.ToString(),
                        _ => null,
                    };
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