using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE019 — Two or more attributes deriving from the same platform base attribute on a
    ///     single property. Preset-attribute inheritance is one-base-per-property by design:
    ///     stacking two <see cref="Vion.Dale.Sdk.Core.ServicePropertyAttribute" /> subclasses
    ///     (e.g. <c>[Kilowatts][Volts]</c>) leaves the cross-fill rule with no winner.
    ///     <para />
    ///     Distinct platform bases on the same property (e.g.
    ///     <c>[ServiceProperty][ServiceMeasuringPoint]</c>) are allowed — they intentionally
    ///     drive the cross-fill / dual-role pattern.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MultipleAttributesFromSameBaseAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] PlatformBases =
        {
            AnalyzerHelper.ServicePropertyAttribute,
            AnalyzerHelper.ServiceMeasuringPointAttribute,
            AnalyzerHelper.PresentationAttribute,
            "Vion.Dale.Sdk.Core.StructFieldAttribute",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE019_MultipleAttributesFromSameBase);
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
            var attrs = property.GetAttributes();

            foreach (var platformBase in PlatformBases)
            {
                var matches = new List<AttributeData>();

                foreach (var attr in attrs)
                {
                    if (attr.AttributeClass is { } cls && DerivesFromOrIs(cls, platformBase))
                    {
                        matches.Add(attr);
                    }
                }

                if (matches.Count <= 1)
                {
                    continue;
                }

                var names = string.Join(", ", matches.Select(a => "[" + (a.AttributeClass?.Name ?? "?") + "]"));
                var baseShortName = platformBase.Substring(platformBase.LastIndexOf('.') + 1);

                foreach (var attr in matches)
                {
                    var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                                ?? property.Locations.FirstOrDefault();

                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE019_MultipleAttributesFromSameBase,
                                                               location,
                                                               property.Name,
                                                               baseShortName,
                                                               names));
                }
            }
        }

        private static bool DerivesFromOrIs(INamedTypeSymbol type, string baseFullName)
        {
            for (var t = (INamedTypeSymbol?)type; t != null; t = t.BaseType)
            {
                if (AnalyzerHelper.GetFullName(t) == baseFullName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
