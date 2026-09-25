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
    ///     Two places write an interval: a member's emission attribute, where only an assigned value is
    ///     checked, and a struct's <c>[DefaultMinInterval]</c>, whose constructor argument always is.
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
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;

            foreach (var attribute in EmissionAttributeHelper.GetEmissionAttributes(property))
            {
                // Only an assigned MinInterval is checked: an omitted one takes a default that is valid, or that
                // is checked where it is declared.
                var minInterval = EmissionAttributeHelper.GetExplicitMinInterval(attribute);
                if (minInterval != null)
                {
                    Validate(context, minInterval, EmissionAttributeHelper.LocationOf(attribute, property), property.Name);
                }
            }
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            var attribute = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.DefaultMinIntervalAttribute);
            if (attribute == null || attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string minInterval)
            {
                return;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? type.Locations.FirstOrDefault();
            Validate(context, minInterval, location, type.Name);
        }

        private static void Validate(SymbolAnalysisContext context, string minInterval, Location? location, string declarer)
        {
            if (!EmissionAttributeHelper.TryParseDuration(minInterval, out var ticks))
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE036_MinIntervalInvalid, location, declarer, minInterval));
                return;
            }

            // ticks == 0 is the throttle-disabling sentinel ("0" / "0ms") — valid, no diagnostic.
            if (ticks > 0 && ticks < OneMillisecondInTicks)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE037_MinIntervalBelowFloor, location, declarer, minInterval));
            }
        }
    }
}