using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE006 — [StatusIndicator] should only be placed on enum-typed properties.
    ///     On non-enum properties, the StatusMappings annotation is silently absent.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StatusIndicatorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;

            if (!AnalyzerHelper.HasAttribute(property, AnalyzerHelper.StatusIndicatorAttribute))
            {
                return;
            }

            if (property.Type.TypeKind == TypeKind.Enum)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}