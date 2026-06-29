using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Topologies
{
    /// <summary>
    ///     A catalog entry for a single logic-block type — the per-block matching metadata a topology-authoring
    ///     client (RFC 0013 Phase 1) needs to compute wiring. Built purely by reflection over the <see cref="Type" />
    ///     (no host build, no instantiation), so it can describe every block the running DevHost references — even
    ///     ones not in the wired configuration. The field shapes mirror the introspection result's
    ///     <c>InterfaceTypeFullNames</c> / <c>MatchingInterfaceTypeFullNames</c> / <c>MatchingContractType</c> so the
    ///     client joins catalog entries and the wired <c>/api/configuration</c> identically.
    /// </summary>
    public sealed class LogicBlockDefinition
    {
        /// <summary>The block's CLR type full name — what a topology file's <c>typeFullName</c> resolves (RFC 0006 R5).</summary>
        public required string TypeFullName { get; set; }

        public required IReadOnlyList<DefinitionInterface> Interfaces { get; set; }

        public required IReadOnlyList<DefinitionContract> Contracts { get; set; }

        /// <summary>
        ///     Build the catalog entry for <paramref name="type" /> by reflection alone — no instantiation, no host
        ///     build. Reuses <see cref="DevConfigurationBuilder" />'s interface/multiplicity/contract reflection so the
        ///     catalog and the wired introspection agree.
        /// </summary>
        public static LogicBlockDefinition FromType(Type type)
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
                                                                })
                                                   .ToList();

            return new LogicBlockDefinition
                   {
                       TypeFullName = type.FullName ?? type.Name,
                       Interfaces = interfaces,
                       Contracts = contracts,
                   };
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
    }

    /// <summary>A service-provider contract a catalog block declares, with the provider-side contract type it binds to.</summary>
    public sealed class DefinitionContract
    {
        public required string Identifier { get; set; }

        public required string MatchingContractType { get; set; }
    }
}