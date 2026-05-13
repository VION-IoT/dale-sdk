using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE023 — <c>[Presentation(UiHint = UiHints.Trigger)]</c> requires a writable bool property.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TriggerHintRequiresBoolAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE023_TriggerHintRequiresBool);
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

            var presentation = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.PresentationAttribute);
            if (presentation == null)
            {
                return;
            }

            var uiHint = AnalyzerHelper.GetNamedArgument<string>(presentation, "UiHint");
            if (uiHint != "trigger")
            {
                return;
            }

            var isBool = property.Type.SpecialType == SpecialType.System_Boolean;
            var hasSetter = property.SetMethod != null;

            if (isBool && hasSetter)
            {
                return;
            }

            var reason = !isBool
                             ? $"type '{property.Type.ToDisplayString()}' is not bool"
                             : "the property is read-only";

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE023_TriggerHintRequiresBool,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       reason));
        }
    }
}
