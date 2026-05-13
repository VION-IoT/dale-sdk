using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE028 — Sentinel format value <c>"relative"</c> requires a <c>DateTime</c> property;
    ///     <c>"humanize"</c> requires a <c>TimeSpan</c>. On a mismatched property type the
    ///     renderer falls back to the default formatter.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FormatSentinelTypeMismatchAnalyzer : DiagnosticAnalyzer
    {
        private const string RelativeSentinel = "relative";

        private const string HumanizeSentinel = "humanize";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE028_FormatSentinelTypeMismatch);
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

            string? format = null;
            foreach (var kvp in presentation.NamedArguments)
            {
                if (kvp.Key == "Format" && kvp.Value.Value is string s)
                {
                    format = s;
                    break;
                }
            }

            if (format == null)
            {
                return;
            }

            var unwrapped = UnwrapNullable(property.Type);
            var isDateTime = unwrapped.SpecialType == SpecialType.System_DateTime;
            var isTimeSpan = unwrapped.ToDisplayString() == "System.TimeSpan";

            if (format == RelativeSentinel && !isDateTime)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE028_FormatSentinelTypeMismatch,
                                                           property.Locations.FirstOrDefault(),
                                                           property.Name,
                                                           format,
                                                           "DateTime",
                                                           property.Type.ToDisplayString()));
            }
            else if (format == HumanizeSentinel && !isTimeSpan)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE028_FormatSentinelTypeMismatch,
                                                           property.Locations.FirstOrDefault(),
                                                           property.Name,
                                                           format,
                                                           "TimeSpan",
                                                           property.Type.ToDisplayString()));
            }
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                return nt.TypeArguments[0];
            }

            return type;
        }
    }
}
