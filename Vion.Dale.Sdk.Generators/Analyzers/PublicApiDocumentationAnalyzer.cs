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
                if (type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                // Skip types from referenced assemblies — only analyze source types
                if (!type.Locations.Any(loc => loc.IsInSource))
                {
                    continue;
                }

                var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
                var matchedNamespace = publicApiNamespaces.FirstOrDefault(pn => ns == pn || ns.StartsWith(pn + "."));
                var inPublicApiNamespace = matchedNamespace != null;

                if (inPublicApiNamespace)
                {
                    namespacesWithTypes.Add(matchedNamespace!);
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

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                yield return type;
            }

            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(child))
                {
                    yield return type;
                }
            }
        }
    }
}
