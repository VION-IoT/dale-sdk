using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Enforces documentation quality for the public API surface.
    ///     DALE013 — [PublicApi] type without XML summary.
    ///     DALE014 — Public type in [PublicApiNamespace] without [PublicApi] or [InternalApi].
    ///     DALE015 — [PublicApiNamespace] references namespace with no public types.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PublicApiDocumentationAnalyzer : DiagnosticAnalyzer
    {
        private const string PublicApiAttributeName = "Vion.Dale.Sdk.Core.PublicApiAttribute";

        private const string InternalApiAttributeName = "Vion.Dale.Sdk.Core.InternalApiAttribute";

        private const string PublicApiNamespaceAttributeName = "Vion.Dale.Sdk.Core.PublicApiNamespaceAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE013_PublicApiMissingDocs, DaleDiagnostics.DALE014_UnmarkedPublicType, DaleDiagnostics.DALE015_StalePublicApiNamespace);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(AnalyzeCompilation);
        }

        private static void AnalyzeCompilation(CompilationAnalysisContext context)
        {
            var compilation = context.Compilation;

            // Read [PublicApiNamespace] from assembly attributes
            var publicApiNamespaces = compilation.Assembly
                                                 .GetAttributes()
                                                 .Where(a => AnalyzerHelper.GetFullName(a.AttributeClass) == PublicApiNamespaceAttributeName)
                                                 .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
                                                 .Where(ns => ns != null)
                                                 .Cast<string>()
                                                 .ToImmutableHashSet();

            // Track which configured namespaces had any public types (for DALE015)
            var namespacesWithTypes = new HashSet<string>();

            // Scan all named types defined in source (not from referenced assemblies)
            foreach (var type in GetAllTypes(compilation.GlobalNamespace))
            {
                // Effective, not declared: a public type nested in an internal one reports
                // DeclaredAccessibility == Public while nothing outside the assembly can name it, so
                // asking it for a mark would be a warning with no action behind it.
                if (!IsEffectivelyPublic(type))
                {
                    continue;
                }

                // Skip types from referenced assemblies — only analyze source types
                if (!type.Locations.Any(loc => loc.IsInSource))
                {
                    continue;
                }

                var ns = type.ContainingNamespace?.ToDisplayString() ?? "";

                // Credit EVERY declaration this type's namespace matches. One declaration can subsume
                // another ("Api" and "Api.Sub"), and the set is unordered, so crediting only the first
                // match reported an arbitrary one of them as stale while it had types all along.
                var inPublicApiNamespace = false;
                foreach (var configured in publicApiNamespaces)
                {
                    if (ns != configured && !ns.StartsWith(configured + ".", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    inPublicApiNamespace = true;
                    namespacesWithTypes.Add(configured);
                }

                var hasPublicApi = AnalyzerHelper.HasAttribute(type, PublicApiAttributeName);
                var hasInternalApi = AnalyzerHelper.HasAttribute(type, InternalApiAttributeName);

                // DALE013: [PublicApi] without XML docs
                if (hasPublicApi)
                {
                    var xml = type.GetDocumentationCommentXml();
                    if (xml == null || !xml.Contains("<summary>"))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE013_PublicApiMissingDocs, type.Locations.FirstOrDefault(), type.Name));
                    }
                }

                // DALE014: public type in API namespace without either attribute
                if (inPublicApiNamespace && !hasPublicApi && !hasInternalApi)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE014_UnmarkedPublicType, type.Locations.FirstOrDefault(), type.Name, ns));
                }
            }

            // DALE015: stale namespace config
            foreach (var ns in publicApiNamespaces)
            {
                if (!namespacesWithTypes.Contains(ns))
                {
                    var attr = compilation.Assembly
                                          .GetAttributes()
                                          .First(a => AnalyzerHelper.GetFullName(a.AttributeClass) == PublicApiNamespaceAttributeName && a.ConstructorArguments.Length > 0 &&
                                                      a.ConstructorArguments[0].Value as string == ns);
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE015_StalePublicApiNamespace, attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(), ns));
                }
            }
        }

        /// <summary>
        ///     Every named type declared under <paramref name="ns" />, nested types included. A public type
        ///     nested in a public type is public API — it reaches the manifest and the docs site — so both
        ///     rules judge it exactly as they judge a top-level one.
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                yield return type;

                foreach (var nested in GetNestedTypes(type))
                {
                    yield return nested;
                }
            }

            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(child))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                yield return nested;

                foreach (var deeper in GetNestedTypes(nested))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>
        ///     Whether <paramref name="type" /> is reachable from outside the assembly: public itself and
        ///     public all the way out through its containing types.
        /// </summary>
        private static bool IsEffectivelyPublic(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }
            }

            return true;
        }
    }
}