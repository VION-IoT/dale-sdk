using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE022 — <c>[ServiceProperty(WriteOnly = true)]</c> restricted to <c>string</c> / <c>string?</c> in v1.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WriteOnlyTypeRestrictionAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE022_WriteOnlyTypeRestriction);
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

            var sp = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
            if (sp == null)
            {
                return;
            }

            var writeOnly = AnalyzerHelper.GetNamedArgument<bool>(sp, "WriteOnly");
            if (!writeOnly)
            {
                return;
            }

            if (property.Type.SpecialType == SpecialType.System_String)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE022_WriteOnlyTypeRestriction,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString()));
        }
    }
}
