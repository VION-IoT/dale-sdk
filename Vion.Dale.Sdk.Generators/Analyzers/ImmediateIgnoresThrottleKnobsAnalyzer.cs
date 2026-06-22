using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE038 — <c>Immediate = true</c> bypasses the throttle and the deadband, so a non-default
    ///     <c>MinInterval</c> or any <c>MinChange</c> declared alongside it is silently ignored. An
    ///     explicit <c>MinInterval = "250ms"</c> (the default echoed) is harmless redundancy and not flagged.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImmediateIgnoresThrottleKnobsAnalyzer : DiagnosticAnalyzer
    {
        private const string DefaultMinInterval = "250ms";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs);
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

            if (!EmissionAttributeHelper.GetImmediate(attribute))
            {
                return;
            }

            var ignoredKnobs = new List<string>();

            var explicitMinInterval = EmissionAttributeHelper.GetExplicitMinInterval(attribute);
            if (explicitMinInterval != null && explicitMinInterval != DefaultMinInterval)
            {
                ignoredKnobs.Add($"MinInterval = \"{explicitMinInterval}\"");
            }

            var minChange = EmissionAttributeHelper.GetMinChange(attribute);
            if (minChange != null)
            {
                ignoredKnobs.Add($"MinChange = \"{minChange}\"");
            }

            if (ignoredKnobs.Count == 0)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       string.Join(" and ", ignoredKnobs)));
        }
    }
}
