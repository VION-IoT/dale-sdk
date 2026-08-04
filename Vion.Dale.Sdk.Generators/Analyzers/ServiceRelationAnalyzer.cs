using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE045 — enforces the discipline of <c>[ServiceRelation]</c> (RFC 0019 §4.6). Relations are
    ///     declared once on the <c>[LogicBlockContract]</c> that both sides of a wire already share; the SDK
    ///     derives one half per bound interface endpoint. Everything this analyzer reports is otherwise
    ///     invisible: the two bind-time throws surface here in-IDE instead of at <c>dale build</c>, and the
    ///     two warnings cover cases the runtime cannot see at all.
    ///     <para>
    ///         Errors: the attribute on a non-contract class; an <c>OutwardsInterface</c> naming neither
    ///         contract side; a duplicate <c>RelationType</c> on one contract (duplicate rows crash the cloud
    ///         activation projection on its composite primary key); empty / whitespace values.
    ///     </para>
    ///     <para>
    ///         Warnings: a component property whose type implements a relation-bearing contract interface but
    ///         has no service surface — its endpoint wires normally yet emits no relation half, because a
    ///         service-less component has no node in the cloud graph to anchor the edge to; and the same
    ///         <c>RelationType</c> on two contracts in one compilation, legitimate for contract versioning
    ///         but a collision when both are wired between the same two services.
    ///     </para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ServiceRelationAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE045_ServiceRelationDiscipline);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // One whole-compilation pass: the cross-contract duplicate check needs every declaration at once,
            // and the component-property check needs to resolve contracts that may live in another assembly.
            context.RegisterCompilationAction(AnalyzeCompilation);
        }

        private static void AnalyzeCompilation(CompilationAnalysisContext context)
        {
            var sourceTypes = GetAllTypes(context.Compilation.GlobalNamespace)
                              .Where(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, context.Compilation.Assembly))
                              .ToList();

            // relationType → the contracts declaring it, for the cross-contract collision warning.
            var declaringContracts = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

            // The short interface names of relation-bearing contracts declared in THIS compilation — the
            // by-name half of the endpoint lookup (see RelationBearingInterfaces).
            var localRelationInterfaceNames = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var type in sourceTypes)
            {
                AnalyzeRelationDeclarations(context, type, declaringContracts, localRelationInterfaceNames);
            }

            foreach (var type in sourceTypes)
            {
                ReportCrossContractCollisions(context, type, declaringContracts);
                AnalyzeComponentProperties(context, type, localRelationInterfaceNames);
            }
        }

        // ── Declarations on the contract ──────────────────────────────────────────────────────────

        private static void AnalyzeRelationDeclarations(CompilationAnalysisContext context,
                                                        INamedTypeSymbol type,
                                                        Dictionary<string, List<string>> declaringContracts,
                                                        HashSet<string> localRelationInterfaceNames)
        {
            var relations = GetRelationAttributes(type);
            if (relations.Count == 0)
            {
                return;
            }

            var contract = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.LogicBlockContractAttribute);
            if (contract is null)
            {
                foreach (var relation in relations)
                {
                    Report(context,
                           AttributeLocation(relation, type),
                           type.Name,
                           "[ServiceRelation] must be declared on a [LogicBlockContract] class — the relation's two sides are the contract's BetweenInterface / AndInterface.");
                }

                return;
            }

            var betweenInterface = AnalyzerHelper.GetNamedArgument<string>(contract, "BetweenInterface");
            var andInterface = AnalyzerHelper.GetNamedArgument<string>(contract, "AndInterface");
            var seenRelationTypes = new HashSet<string>(System.StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(betweenInterface))
            {
                localRelationInterfaceNames.Add(betweenInterface!);
            }

            if (!string.IsNullOrWhiteSpace(andInterface))
            {
                localRelationInterfaceNames.Add(andInterface!);
            }

            foreach (var relation in relations)
            {
                var location = AttributeLocation(relation, type);
                var relationType = AnalyzerHelper.GetNamedArgument<string>(relation, "RelationType");
                var outwardsInterface = AnalyzerHelper.GetNamedArgument<string>(relation, "OutwardsInterface");

                if (string.IsNullOrWhiteSpace(relationType))
                {
                    Report(context, location, type.Name, "RelationType must be a non-empty identifier — it is the stable cloud-API / UI key the relation is matched on.");
                }
                else if (!seenRelationTypes.Add(relationType!))
                {
                    Report(context,
                           location,
                           type.Name,
                           $"RelationType \"{relationType}\" is declared twice on this contract — each declaration derives its own half, so the duplicate would produce " +
                           "rows identical in every primary-key column and crash the cloud activation projection.");
                }
                else
                {
                    // Only well-formed, non-duplicate declarations feed the cross-contract collision check.
                    if (!declaringContracts.TryGetValue(relationType!, out var contracts))
                    {
                        contracts = [];
                        declaringContracts[relationType!] = contracts;
                    }

                    contracts.Add(type.Name);
                }

                if (string.IsNullOrWhiteSpace(outwardsInterface))
                {
                    Report(context, location, type.Name, "OutwardsInterface must name one of the contract's two interfaces — it is what picks the subordinate / providing side.");
                }
                else if (outwardsInterface != betweenInterface && outwardsInterface != andInterface)
                {
                    Report(context,
                           location,
                           type.Name,
                           $"OutwardsInterface \"{outwardsInterface}\" is neither the contract's BetweenInterface (\"{betweenInterface}\") nor its AndInterface (\"{andInterface}\").");
                }
            }
        }

        private static void ReportCrossContractCollisions(CompilationAnalysisContext context, INamedTypeSymbol type, Dictionary<string, List<string>> declaringContracts)
        {
            foreach (var relation in GetRelationAttributes(type))
            {
                var relationType = AnalyzerHelper.GetNamedArgument<string>(relation, "RelationType");
                if (string.IsNullOrWhiteSpace(relationType) || !declaringContracts.TryGetValue(relationType!, out var contracts) || contracts.Count < 2)
                {
                    continue;
                }

                var others = contracts.Where(c => c != type.Name).Distinct().OrderBy(c => c, System.StringComparer.Ordinal).ToList();
                if (others.Count == 0)
                {
                    continue;
                }

                ReportWarning(context,
                              AttributeLocation(relation, type),
                              type.Name,
                              $"RelationType \"{relationType}\" is also declared on {string.Join(", ", others)}. That is fine for contract versioning, but if both " +
                              "contracts are wired between the same two services the resulting rows collide on the cloud's composite primary key.");
            }
        }

        // ── Component properties ──────────────────────────────────────────────────────────────────

        private static void AnalyzeComponentProperties(CompilationAnalysisContext context, INamedTypeSymbol type, HashSet<string> localRelationInterfaceNames)
        {
            // Property-based interface binding only happens on the logic-block class itself.
            if (type.TypeKind != TypeKind.Class || !AnalyzerHelper.InheritsFromLogicBlockBase(type))
            {
                return;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic || property.Type is not INamedTypeSymbol propertyType)
                {
                    continue;
                }

                var relationBearing = RelationBearingInterfaces(propertyType, localRelationInterfaceNames, context.CancellationToken);
                if (relationBearing.Count == 0 || AnalyzerHelper.IsServiceBearing(propertyType))
                {
                    continue;
                }

                ReportWarning(context,
                              property.Locations.FirstOrDefault() ?? Location.None,
                              property.Name,
                              $"its type '{propertyType.Name}' implements the relation-bearing contract interface(s) {string.Join(", ", relationBearing)} but has no service " +
                              "surface, so this endpoint wires normally yet emits no relation half. Give the component a [ServiceProperty] (or a [ServiceInterface]) so it " +
                              "becomes a service, or implement the interface on the logic-block class for a block-granularity edge.");
            }
        }

        /// <summary>
        ///     The relation-bearing contract interfaces <paramref name="type" /> implements, by short name.
        ///     Two lookups, because neither alone is complete:
        ///     <list type="bullet">
        ///         <item>
        ///             <b>By symbol</b> — walk <see cref="ITypeSymbol.AllInterfaces" /> to
        ///             <c>[LogicInterface].ContractType</c> and check it for <c>[ServiceRelation]</c>. This is
        ///             the accurate path, and the only one that reaches a contract in a <i>referenced</i>
        ///             assembly.
        ///         </item>
        ///         <item>
        ///             <b>By name</b> — match the type's declared base list against the interface names of
        ///             this compilation's own relation-bearing contracts. Necessary because a contract's
        ///             interfaces are emitted by <c>LogicClassGenerator</c>, and in this repo's build the
        ///             generator output is not part of the compilation analyzers see: a
        ///             <c>class ChargePoint : IConsumer</c> resolves <c>IConsumer</c> to an error type (or
        ///             drops it from <c>AllInterfaces</c> entirely), so the symbol path finds nothing for the
        ///             common same-library case. Contracts name their sides by string anyway, so matching by
        ///             name is in keeping with the model rather than a workaround around it.
        ///         </item>
        ///     </list>
        /// </summary>
        private static List<string> RelationBearingInterfaces(INamedTypeSymbol type, HashSet<string> localRelationInterfaceNames, CancellationToken cancellationToken)
        {
            var result = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var iface in type.AllInterfaces)
            {
                var logicInterface = AnalyzerHelper.GetAttribute(iface, AnalyzerHelper.LogicInterfaceAttribute);
                if (logicInterface is null)
                {
                    continue;
                }

                if (AnalyzerHelper.GetNamedArgument<INamedTypeSymbol>(logicInterface, "ContractType") is { } contractType && GetRelationAttributes(contractType).Count > 0)
                {
                    result.Add(iface.Name);
                }
            }

            if (localRelationInterfaceNames.Count > 0)
            {
                foreach (var baseTypeName in DeclaredBaseTypeNames(type, cancellationToken))
                {
                    if (localRelationInterfaceNames.Contains(baseTypeName))
                    {
                        result.Add(baseTypeName);
                    }
                }
            }

            return result.OrderBy(n => n, System.StringComparer.Ordinal).ToList();
        }

        /// <summary>The simple names in <paramref name="type" />'s declared base list, across all its parts.</summary>
        private static IEnumerable<string> DeclaredBaseTypeNames(INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration || declaration.BaseList is null)
                {
                    continue;
                }

                foreach (var baseType in declaration.BaseList.Types)
                {
                    var name = baseType.Type switch
                    {
                        SimpleNameSyntax simple => simple.Identifier.ValueText,
                        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                        _ => null,
                    };

                    if (name != null)
                    {
                        yield return name;
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        private static List<AttributeData> GetRelationAttributes(ISymbol symbol)
        {
            return symbol.GetAttributes().Where(a => AnalyzerHelper.GetFullName(a.AttributeClass) == AnalyzerHelper.ServiceRelationAttribute).ToList();
        }

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol childNamespace:
                        foreach (var nested in GetAllTypes(childNamespace))
                        {
                            yield return nested;
                        }

                        break;
                    case INamedTypeSymbol namedType:
                        yield return namedType;

                        foreach (var nested in GetAllTypes(namedType))
                        {
                            yield return nested;
                        }

                        break;
                }
            }
        }

        private static Location AttributeLocation(AttributeData attribute, ISymbol fallback)
        {
            return attribute.ApplicationSyntaxReference?.GetSyntax()?.GetLocation() ?? fallback.Locations.FirstOrDefault() ?? Location.None;
        }

        private static void Report(CompilationAnalysisContext context, Location location, string subject, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE045_ServiceRelationDiscipline, location, subject, message));
        }

        /// <summary>
        ///     Reports DALE045 at warning severity. Same ID as the errors by design (RFC 0019 §4.6) — an
        ///     advisory finding about the same rule family, not a separate one.
        /// </summary>
        private static void ReportWarning(CompilationAnalysisContext context, Location location, string subject, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE045_ServiceRelationDiscipline,
                                                       location,
                                                       DiagnosticSeverity.Warning,
                                                       null,
                                                       null,
                                                       subject,
                                                       message));
        }
    }
}