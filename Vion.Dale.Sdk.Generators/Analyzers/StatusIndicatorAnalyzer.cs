using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE006 — [StatusIndicator] should only be placed on enum-typed properties.
    ///     On non-enum properties, the StatusMappings annotation is silently absent.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StatusIndicatorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum);
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

            if (!AnalyzerHelper.HasAttribute(property, AnalyzerHelper.StatusIndicatorAttribute))
            {
                return;
            }

            // Accept both `MyEnum` and `MyEnum?`. The runtime introspection (see
            // PropertyMetadataBuilder.ExtractStatusMappings) unwraps Nullable<T> before
            // checking IsEnum, so a nullable-enum [StatusIndicator] is legitimate —
            // the previous strict TypeKind check produced false-positive DALE006s.
            var underlying = UnwrapNullable(property.Type);
            if (underlying.TypeKind == TypeKind.Enum)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE006_StatusIndicatorRequiresEnum,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                return named.TypeArguments[0];
            }

            return type;
        }
    }
}
