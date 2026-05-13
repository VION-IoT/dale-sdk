using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE021 — <c>[Presentation(Decimals = N)]</c> only meaningful on numeric properties.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DecimalsOnNonNumericAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE021_DecimalsOnNonNumeric);
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

            // Only fire if Decimals was set explicitly. NamedArguments only contains keys the user
            // actually wrote — an absent entry means the attribute property uses its default
            // (int.MinValue sentinel = "unset"). `GetNamedArgument<int>` returns default(int) = 0
            // for missing keys, which would collide with a legitimate Decimals = 0 usage, so look
            // at NamedArguments directly.
            var hasDecimals = false;
            foreach (var kvp in presentation.NamedArguments)
            {
                if (kvp.Key == "Decimals")
                {
                    hasDecimals = true;
                    break;
                }
            }
            if (!hasDecimals)
            {
                return;
            }

            if (IsNumericType(property.Type))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE021_DecimalsOnNonNumeric,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       property.Type.ToDisplayString()));
        }

        private static bool IsNumericType(ITypeSymbol type)
        {
            // Unwrap Nullable<T>.
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                return IsNumericType(nt.TypeArguments[0]);
            }

            return type.SpecialType is SpecialType.System_Byte
                                    or SpecialType.System_SByte
                                    or SpecialType.System_Int16
                                    or SpecialType.System_UInt16
                                    or SpecialType.System_Int32
                                    or SpecialType.System_UInt32
                                    or SpecialType.System_Int64
                                    or SpecialType.System_UInt64
                                    or SpecialType.System_Single
                                    or SpecialType.System_Double
                                    or SpecialType.System_Decimal;
        }
    }
}
