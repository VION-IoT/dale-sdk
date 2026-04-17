using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE007 — [Persistent] on a read-only property has no effect.
    ///     PersistentData silently skips properties without setters.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PersistentPropertyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE007_PersistentRequiresSetter);
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

            var persistentAttr = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.PersistentAttribute);
            if (persistentAttr == null)
            {
                return;
            }

            // If Exclude = true, [Persistent] is an opt-out marker — no warning needed
            var exclude = AnalyzerHelper.GetNamedArgument<bool>(persistentAttr, "Exclude");
            if (exclude)
            {
                return;
            }

            // Property must have some setter (any accessibility)
            if (property.SetMethod != null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE007_PersistentRequiresSetter, property.Locations.FirstOrDefault(), property.Name));
        }
    }
}