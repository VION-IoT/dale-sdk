using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE036 / DALE037 — <c>MinInterval</c> validation. An unparseable duration is an error
    ///     (DALE036); a positive value below the 1 ms floor the emission gate can honour is a warning
    ///     (DALE037). The throttle-disabling sentinel <c>"0"</c> / <c>"0ms"</c> is valid and never reported.
    ///     Only the explicitly-written value is checked — an omitted <c>MinInterval</c> uses the valid
    ///     <c>"250ms"</c> default.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MinIntervalInvalidAnalyzer : DiagnosticAnalyzer
    {
        // The emission gate's trailing-edge flush rides the actor scheduler; 1 ms (= 10_000 ticks) is the
        // smallest interval it can meaningfully honour.
        private const long OneMillisecondInTicks = 10_000;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE036_MinIntervalInvalid, DaleDiagnostics.DALE037_MinIntervalBelowFloor);
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

            // Only fire on an explicitly-written MinInterval; the default "250ms" is valid and an omitted
            // value is indistinguishable from an explicit "250ms" (so nothing to flag).
            var minInterval = EmissionAttributeHelper.GetExplicitMinInterval(attribute);
            if (minInterval == null)
            {
                return;
            }

            if (!EmissionAttributeHelper.TryParseDuration(minInterval, out var ticks))
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE036_MinIntervalInvalid, property.Locations.FirstOrDefault(), property.Name, minInterval));
                return;
            }

            // ticks == 0 is the throttle-disabling sentinel ("0" / "0ms") — valid, no diagnostic.
            if (ticks > 0 && ticks < OneMillisecondInTicks)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE037_MinIntervalBelowFloor, property.Locations.FirstOrDefault(), property.Name, minInterval));
            }
        }
    }
}