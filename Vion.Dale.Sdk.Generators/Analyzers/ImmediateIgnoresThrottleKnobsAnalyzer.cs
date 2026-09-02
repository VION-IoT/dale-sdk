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

            foreach (var attribute in EmissionAttributeHelper.GetEmissionAttributes(property))
            {
                if (!EmissionAttributeHelper.GetImmediate(attribute))
                {
                    continue;
                }

                var ignoredKnobs = new List<string>();

                // Compare the declared interval as a DURATION, not as a spelling: "250" and "250ms"
                // configure the same gate, so warning about one and not the other would report a knob as
                // ignored on the strength of how it was typed.
                var explicitMinInterval = EmissionAttributeHelper.GetExplicitMinInterval(attribute);
                if (explicitMinInterval != null && !EmissionAttributeHelper.IsDefaultInterval(explicitMinInterval))
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
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs,
                                                           property.Locations.FirstOrDefault(),
                                                           property.Name,
                                                           string.Join(" and ", ignoredKnobs)));
            }
        }
    }
}