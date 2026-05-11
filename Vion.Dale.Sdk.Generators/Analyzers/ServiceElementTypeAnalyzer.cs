using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE003 — [ServiceProperty] and [ServiceMeasuringPoint] must use supported types.
    ///     Supported: bool, string, int, long, short, float, double, decimal, DateTime, TimeSpan, or any enum.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ServiceElementTypeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE003_UnsupportedServicePropertyType);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;

            var hasServiceProperty = AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
            var hasServiceMeasuringPoint = AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);

            if (!hasServiceProperty && !hasServiceMeasuringPoint)
            {
                return;
            }

            if (AnalyzerHelper.IsSupportedServiceElementType(property.Type))
            {
                return;
            }

            var attributeName = hasServiceProperty ? "ServiceProperty" : "ServiceMeasuringPoint";
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE003_UnsupportedServicePropertyType,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       attributeName,
                                                       property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
