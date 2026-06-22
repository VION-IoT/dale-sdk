using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE034 — <c>MinChange</c> (the deadband) on a <c>[ServiceProperty]</c> /
    ///     <c>[ServiceMeasuringPoint]</c> whose value type has no resolvable
    ///     <c>IChangeThreshold&lt;T&gt;</c>. Built-ins exist for double/float/decimal/int/long/TimeSpan;
    ///     any other type needs an <c>IChangeThreshold&lt;ThatType&gt;</c> implementation visible in the
    ///     compilation. <c>bool</c> (no magnitude) is always an error.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MinChangeWithoutChangeThresholdAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold);
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

            // The runtime keys the registry on the member's value type. Unwrap Nullable<T> so the
            // author's intent (a double deadband on a double? property) is honoured — the underlying
            // type is what carries the magnitude.
            var valueType = EmissionAttributeHelper.Unwrap(property.Type);

            if (EmissionAttributeHelper.IsBuiltInThresholdType(valueType))
            {
                return;
            }

            if (EmissionAttributeHelper.HasChangeThresholdFor(context.Compilation, valueType))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       valueType.ToDisplayString()));
        }
    }
}
