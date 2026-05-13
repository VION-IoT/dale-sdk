using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE027 — <c>[Presentation(Format = "...")]</c> is consumed by the renderer only for
    ///     <c>DateTime</c> / <c>TimeSpan</c> properties (and nullable variants). On other types
    ///     the format hint is silently ignored.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FormatOnNonTemporalAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE027_FormatOnNonTemporal);
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

            if (!HasFormatSet(presentation))
            {
                return;
            }

            if (IsTemporalType(property.Type))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE027_FormatOnNonTemporal,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString()));
        }

        private static bool HasFormatSet(AttributeData attr)
        {
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == "Format" && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsTemporalType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                return IsTemporalType(nt.TypeArguments[0]);
            }

            return type.SpecialType == SpecialType.System_DateTime
                || type.ToDisplayString() == "System.TimeSpan";
        }
    }
}
