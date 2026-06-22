using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE035 — <c>MinChange</c> on a built-in numeric type or <c>TimeSpan</c> does not parse with
    ///     that type's known format. Numeric types need an invariant-culture number; a <c>TimeSpan</c>
    ///     needs the duration grammar. Custom-threshold types are not parse-checked (opaque format).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MinChangeUnparseableAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE035_MinChangeUnparseable);
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

            var attribute = EmissionAttributeHelper.GetEmissionAttribute(property);
            if (attribute == null)
            {
                return;
            }

            var minChange = EmissionAttributeHelper.GetMinChange(attribute);
            if (minChange == null)
            {
                return;
            }

            var valueType = EmissionAttributeHelper.Unwrap(property.Type);

            // Only the built-in numeric / TimeSpan formats are known. For every other type the MinChange
            // format is opaque (interpreted by a custom IChangeThreshold<T>), so we never parse-check.
            if (!EmissionAttributeHelper.TryGetParseExpectation(valueType, minChange, out var parses, out var expectationHint))
            {
                return;
            }

            if (parses)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE035_MinChangeUnparseable,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       minChange,
                                                       valueType.ToDisplayString(),
                                                       expectationHint));
        }
    }
}