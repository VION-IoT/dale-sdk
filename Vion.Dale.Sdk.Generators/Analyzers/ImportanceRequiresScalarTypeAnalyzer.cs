using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE032 — <c>[Presentation(Importance = Primary/Secondary)]</c> requires a scalar property type.
    ///     The dashboard LogicBlock tile renders Primary/Secondary metrics as a single scalar value, so
    ///     composite types (flat record struct, <c>ImmutableArray&lt;T&gt;</c>) can't be shown there.
    ///     See <see cref="DaleDiagnostics.DALE032_ImportanceRequiresScalarType" />.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImportanceRequiresScalarTypeAnalyzer : DiagnosticAnalyzer
    {
        // Underlying values of the [PublicApi] enum Vion.Dale.Sdk.Core.Importance (stable order):
        // Normal = 0, Primary = 1, Secondary = 2, Hidden = 3. Only Primary/Secondary surface on the tile.
        private const int Primary = 1;

        private const int Secondary = 2;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE032_ImportanceRequiresScalarType);
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

            var importance = AnalyzerHelper.GetNamedArgument<int>(presentation, "Importance");
            if (importance != Primary && importance != Secondary)
            {
                return;
            }

            // Unwrap Nullable<T> — a nullable composite is still composite.
            var type = property.Type;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                type = nt.TypeArguments[0];
            }

            // Composite = a supported service-element type that isn't scalar — i.e. a flat record struct
            // or ImmutableArray<T>. Unsupported types are already covered by DALE003/DALE008/DALE016, and
            // scalars (numeric / bool / string / enum / DateTime / TimeSpan) render fine on the tile.
            var isComposite = AnalyzerHelper.IsSupportedServiceElementType(type) && !AnalyzerHelper.IsScalarTileType(type);
            if (!isComposite)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE032_ImportanceRequiresScalarType,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       importance == Primary ? "Primary" : "Secondary",
                                                       property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}