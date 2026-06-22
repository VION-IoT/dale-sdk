using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE039 — <c>MinChange</c> (a deadband) is set while <c>MinInterval</c> is the disabling
    ///     sentinel <c>"0"</c> / <c>"0ms"</c>: a valid deadband-only configuration (no time throttle,
    ///     change gate still applies), surfaced as information so the intent is explicit. This is the
    ///     cleanly-detectable "deadband without throttle" case — an *omitted* MinInterval can't be
    ///     distinguished from an explicit <c>"250ms"</c>, so this rule does not attempt it.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeadbandWithoutThrottleAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE039_DeadbandWithoutThrottle);
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

            // Immediate bypasses the deadband as well as the throttle, so "deadband only" doesn't apply
            // (that combination is DALE038's concern).
            if (EmissionAttributeHelper.GetImmediate(attribute))
            {
                return;
            }

            var minInterval = EmissionAttributeHelper.GetExplicitMinInterval(attribute);
            if (minInterval == null || !EmissionAttributeHelper.IsDisablingSentinel(minInterval))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE039_DeadbandWithoutThrottle,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       minInterval));
        }
    }
}
