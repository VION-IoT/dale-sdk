using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE004 — [ServiceMeasuringPoint] should not have a public setter.
    ///     Private setter is fine (needed for Metalama INPC weaving).
    ///     Exception: if the property also has [ServiceProperty], the public setter serves that binding.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MeasuringPointAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DaleDiagnostics.DALE004_MeasuringPointPublicSetter);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;

            if (!AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute))
            {
                return;
            }

            // If also has [ServiceProperty], the public setter is valid for that binding
            if (AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServicePropertyAttribute))
            {
                return;
            }

            // Check for PUBLIC setter specifically (private setter is fine for Metalama INPC)
            if (property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DaleDiagnostics.DALE004_MeasuringPointPublicSetter,
                    property.Locations.FirstOrDefault(),
                    property.Name));
            }
        }
    }
}
