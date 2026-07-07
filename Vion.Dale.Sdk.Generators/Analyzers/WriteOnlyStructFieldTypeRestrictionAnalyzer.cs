using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE040 — <c>[StructField(WriteOnly = true)]</c> restricted to <c>string</c> / <c>string?</c> in
    ///     v1, the per-member analogue of DALE022. <c>[StructField]</c> binds to the record-struct
    ///     primary-constructor parameter (where <c>TypeRefBuilder</c> reads it), so the check runs on
    ///     parameters, not properties.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WriteOnlyStructFieldTypeRestrictionAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE040_WriteOnlyStructFieldTypeRestriction);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeParameter, SymbolKind.Parameter);
        }

        private static void AnalyzeParameter(SymbolAnalysisContext context)
        {
            var parameter = (IParameterSymbol)context.Symbol;

            var structField = AnalyzerHelper.GetAttribute(parameter, AnalyzerHelper.StructFieldAttribute);
            if (structField is null)
            {
                return;
            }

            var writeOnly = AnalyzerHelper.GetNamedArgument<bool>(structField, "WriteOnly");
            if (!writeOnly)
            {
                return;
            }

            // string? shares SpecialType.System_String — nullability is an annotation, so string? passes too.
            if (parameter.Type.SpecialType == SpecialType.System_String)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE040_WriteOnlyStructFieldTypeRestriction,
                                                       parameter.Locations.FirstOrDefault(),
                                                       parameter.Name,
                                                       parameter.Type.ToDisplayString()));
        }
    }
}