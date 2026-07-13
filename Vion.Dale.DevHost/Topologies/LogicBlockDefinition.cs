using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Topologies
{
    /// <summary>
    ///     A catalog entry for a single logic-block type — the per-block matching metadata a topology-authoring
    ///     client (RFC 0013 Phase 1) needs to compute wiring. Built purely by reflection over the <see cref="Type" />
    ///     (no host build; an optional instance is used only to read <c>[InstantiationParameter]</c> defaults), so it
    ///     can describe every block the running DevHost references — even ones not in the wired configuration. The
    ///     field shapes mirror the introspection result's <c>InterfaceTypeFullNames</c> /
    ///     <c>MatchingInterfaceTypeFullNames</c> / <c>MatchingContractType</c> so the client joins catalog entries and
    ///     the wired <c>/api/configuration</c> identically.
    /// </summary>
    public sealed class LogicBlockDefinition
    {
        /// <summary>The block's CLR type full name — what a topology file's <c>typeFullName</c> resolves (RFC 0006 R5).</summary>
        public required string TypeFullName { get; set; }

        public required IReadOnlyList<DefinitionInterface> Interfaces { get; set; }

        public required IReadOnlyList<DefinitionContract> Contracts { get; set; }

        /// <summary>
        ///     RFC 0016: the block's <c>[InstantiationParameter]</c> properties (identifier + JSON schema + default),
        ///     so a topology-authoring client can render a per-instance parameter editor and evaluate the
        ///     <c>[IncludedWhen]</c> gates on <see cref="DefinitionInterface.IncludedWhen" /> /
        ///     <see cref="DefinitionContract.IncludedWhen" /> against the chosen values. Empty when the block declares none.
        /// </summary>
        public IReadOnlyList<DefinitionParameter> InstantiationParameters { get; set; } = [];

        /// <summary>
        ///     Build the catalog entry for <paramref name="type" /> by reflection. Pass an <paramref name="instance" />
        ///     (any instance of the type) to populate each <c>[InstantiationParameter]</c> default; without one the
        ///     defaults are omitted. Reuses <see cref="DevConfigurationBuilder" />'s interface/multiplicity/contract
        ///     reflection so the catalog and the wired introspection agree.
        /// </summary>
        public static LogicBlockDefinition FromType(Type type, object? instance = null)
        {
            var interfaces = DevConfigurationBuilder.GetAllLogicInterfaces(type)
                                                    .Select(i =>
                                                            {
                                                                // The MatchingInterface back-reference is declared on the
                                                                // [LogicInterface] of the interface type itself (the same
                                                                // attribute property DiscoverMatchingInterfaces matches on);
                                                                // emit a single-element list, or empty when unset.
                                                                var matching = i.InterfaceType.GetCustomAttribute<LogicInterfaceAttribute>()?.MatchingInterface?.FullName;

                                                                return new DefinitionInterface
                                                                       {
                                                                           Identifier = i.Identifier,
                                                                           InterfaceTypeFullNames = i.InterfaceType.FullName is { } fullName ? [fullName] : [],
                                                                           MatchingInterfaceTypeFullNames = matching is not null ? [matching] : [],
                                                                           Multiplicity = DevConfigurationBuilder.MultiplicityOf(type, i.Identifier),
                                                                           IncludedWhen = i.IncludedWhen,
                                                                       };
                                                            })
                                                    .ToList();

            // The provider-side contract-type token GetContractProperties returns is exactly what introspection
            // records as ContractInfo.MatchingContractType — so the catalog carries it without instantiating.
            var contracts = DevConfigurationBuilder.GetContractProperties(type)
                                                   .Select(c => new DefinitionContract
                                                                {
                                                                    Identifier = c.Identifier,
                                                                    MatchingContractType = c.ContractType,
                                                                    IncludedWhen = c.IncludedWhen,
                                                                })
                                                   .ToList();

            return new LogicBlockDefinition
                   {
                       TypeFullName = type.FullName ?? type.Name,
                       Interfaces = interfaces,
                       Contracts = contracts,
                       InstantiationParameters = BuildInstantiationParameters(type, instance),
                   };
        }

        // Reflect each [InstantiationParameter] property (a [ServiceProperty] restricted to bool / enum / integer
        // kinds / string) into { identifier, schema, default }. The default needs an instance (a C# `init`
        // initializer is not reflectable); when none is supplied the value is left null and the client leaves the
        // input empty (the runtime then uses the block's own C# default).
        private static IReadOnlyList<DefinitionParameter> BuildInstantiationParameters(Type type, object? instance)
        {
            var result = new List<DefinitionParameter>();

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<InstantiationParameterAttribute>() is null)
                {
                    continue;
                }

                result.Add(new DefinitionParameter
                           {
                               Identifier = property.Name,
                               Schema = BuildParameterSchema(property),
                               Default = instance is not null ? ParameterValueToJson(property.GetValue(instance)) : null,
                           });
            }

            return result;
        }

        // A minimal JSON-schema fragment the client's ValueEditor reads to pick a control: enum → select,
        // integer → number (with the [ServiceProperty] bounds), string → text, bool → checkbox.
        private static JsonNode BuildParameterSchema(PropertyInfo property)
        {
            var schema = new JsonObject();
            var serviceProperty = property.GetCustomAttribute<ServicePropertyAttribute>();

            if (serviceProperty?.Title is { } title)
            {
                schema["title"] = title;
            }

            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (type.IsEnum)
            {
                var members = new JsonArray();
                foreach (var name in Enum.GetNames(type))
                {
                    members.Add(name);
                }

                schema["enum"] = members;
            }
            else if (type == typeof(bool))
            {
                schema["type"] = "boolean";
            }
            else if (type == typeof(string))
            {
                schema["type"] = "string";
            }
            else if (IsIntegerType(type))
            {
                schema["type"] = "integer";
                if (serviceProperty is not null)
                {
                    if (!double.IsInfinity(serviceProperty.Minimum))
                    {
                        schema["minimum"] = (long)serviceProperty.Minimum;
                    }

                    if (!double.IsInfinity(serviceProperty.Maximum))
                    {
                        schema["maximum"] = (long)serviceProperty.Maximum;
                    }
                }
            }
            else
            {
                // [InstantiationParameter] is restricted to the scalar set above; fall back to text defensively.
                schema["type"] = "string";
            }

            return schema;
        }

        // The wire scalar for a parameter value: enum by member name, integer as a number, bool/string as-is —
        // the same JSON shape topology instantiationParameters carry and the predicate evaluator compares against.
        private static JsonNode? ParameterValueToJson(object? value)
        {
            return value switch
            {
                null => null,
                bool b => JsonValue.Create(b),
                string s => JsonValue.Create(s),
                Enum e => JsonValue.Create(e.ToString()),
                _ => JsonValue.Create(Convert.ToInt64(value)),
            };
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(uint) ||
                   type == typeof(ulong) || type == typeof(ushort);
        }
    }

    /// <summary>A logic interface a catalog block exposes, with the frozen cross-repo wiring relation the client matches on.</summary>
    public sealed class DefinitionInterface
    {
        public required string Identifier { get; set; }

        /// <summary>
        ///     The CLR full name(s) of the logic-interface type this entry exposes — a single-element list, shaped to
        ///     match the introspection's list-typed <c>InterfaceTypeFullNames</c> so the client matcher joins identically.
        /// </summary>
        public required IReadOnlyList<string> InterfaceTypeFullNames { get; set; }

        /// <summary>
        ///     The CLR full name(s) of the matching counterpart interface — the <c>[LogicInterface]</c>'s
        ///     <c>MatchingInterface</c> back-reference. Empty when the interface declares no match.
        /// </summary>
        public required IReadOnlyList<string> MatchingInterfaceTypeFullNames { get; set; }

        /// <summary>
        ///     The consumer-side link multiplicity declared on this block's binding for the interface (defaults to
        ///     ZeroOrMore).
        /// </summary>
        public required LinkMultiplicity Multiplicity { get; set; }

        /// <summary>
        ///     RFC 0016: the <c>[IncludedWhen]</c> predicate that gates this (property-based) interface binding, or
        ///     <c>null</c> when ungated (class-level bindings are always unconditional). A client evaluates it against
        ///     the instance's chosen <c>[InstantiationParameter]</c> values to know whether a mapping to this interface
        ///     would target a gated-out endpoint.
        /// </summary>
        public string? IncludedWhen { get; set; }
    }

    /// <summary>A service-provider contract a catalog block declares, with the provider-side contract type it binds to.</summary>
    public sealed class DefinitionContract
    {
        public required string Identifier { get; set; }

        public required string MatchingContractType { get; set; }

        /// <summary>RFC 0016: the <c>[IncludedWhen]</c> predicate gating this contract binding, or <c>null</c> when ungated.</summary>
        public string? IncludedWhen { get; set; }
    }

    /// <summary>An <c>[InstantiationParameter]</c> a catalog block declares — the operator-settable config-time value.</summary>
    public sealed class DefinitionParameter
    {
        public required string Identifier { get; set; }

        /// <summary>A minimal JSON-schema fragment (type/enum/minimum/maximum) the client renders an input from.</summary>
        public required JsonNode Schema { get; set; }

        /// <summary>The block's C# default for this parameter (member-name string for enums), or <c>null</c> when unknown.</summary>
        public JsonNode? Default { get; set; }
    }
}