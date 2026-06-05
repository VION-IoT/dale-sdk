using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE030 — <c>[ServiceProperty(ReadOnly = true, WriteOnly = true)]</c> is incoherent.
    ///     The two flags hide opposite directions of the value flow; the combination has no consistent
    ///     meaning, so reject it at compile time rather than letting one silently win at runtime.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReadOnlyAndWriteOnlyExclusivityAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE030_ReadOnlyAndWriteOnlyMutuallyExclusive);
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

            var readOnly = AnalyzerHelper.GetNamedArgument<bool>(sp, "ReadOnly");
            var writeOnly = AnalyzerHelper.GetNamedArgument<bool>(sp, "WriteOnly");
            if (!readOnly || !writeOnly)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE030_ReadOnlyAndWriteOnlyMutuallyExclusive, property.Locations.FirstOrDefault(), property.Name));
        }
    }
}