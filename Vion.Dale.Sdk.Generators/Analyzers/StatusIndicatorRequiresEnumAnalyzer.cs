using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE024 — <c>[Presentation(StatusIndicator = true)]</c> requires an enum (or nullable-enum) property.
    ///     Replaces the retired DALE006 that targeted the now-deleted <c>[StatusIndicator]</c> attribute.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StatusIndicatorRequiresEnumAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE024_StatusIndicatorRequiresEnum);
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

            var statusIndicator = AnalyzerHelper.GetNamedArgument<bool>(presentation, "StatusIndicator");
            if (!statusIndicator)
            {
                return;
            }

            // Unwrap Nullable<T>.
            var type = property.Type;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                type = nt.TypeArguments[0];
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE024_StatusIndicatorRequiresEnum,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString()));
        }
    }
}
