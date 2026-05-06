using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE016 — A user-defined struct used as a [ServiceProperty] or [ServiceMeasuringPoint]
    ///     type must be a <c>readonly record struct</c> with flat primitive/enum/string fields.
    ///     Regular structs, mutable record structs, and structs with non-flat fields are rejected.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StructServiceElementAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord);
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

            var hasServiceProperty = AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
            var hasServiceMeasuringPoint = AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);

            if (!hasServiceProperty && !hasServiceMeasuringPoint)
            {
                return;
            }

            var badStruct = FindInvalidUserStruct(property.Type);
            if (badStruct is null)
            {
                return;
            }

            var attributeName = hasServiceProperty ? "ServiceProperty" : "ServiceMeasuringPoint";
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE016_StructMustBeFlatReadonlyRecord,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       attributeName,
                                                       badStruct.Name));
        }

        /// <summary>
        ///     Walks the type, peeling <c>Nullable&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c>,
        ///     and returns the first user-defined value type that is NOT a valid flat readonly record struct,
        ///     or <c>null</c> if no invalid struct is found.
        /// </summary>
        private static INamedTypeSymbol? FindInvalidUserStruct(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
            {
                return null;
            }

            // Peel Nullable<T>
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return FindInvalidUserStruct(named.TypeArguments[0]);
            }

            // Peel ImmutableArray<T>
            if (named.Name == "ImmutableArray" && named.ContainingNamespace?.Name == "Immutable")
            {
                return FindInvalidUserStruct(named.TypeArguments[0]);
            }

            // Only check value types (structs)
            if (!named.IsValueType)
            {
                return null;
            }

            // Skip system/built-in value types: primitives, enums, DateTime, TimeSpan, etc.
            if (named.SpecialType != SpecialType.None)
            {
                return null;
            }

            if (named.TypeKind == TypeKind.Enum)
            {
                return null;
            }

            if (named.ToDisplayString() == "System.TimeSpan" || named.ToDisplayString() == "System.DateTime")
            {
                return null;
            }

            // User-defined value type: must be a flat readonly record struct
            if (AnalyzerHelper.IsFlatReadonlyRecordStruct(named))
            {
                return null;
            }

            return named;
        }
    }
}