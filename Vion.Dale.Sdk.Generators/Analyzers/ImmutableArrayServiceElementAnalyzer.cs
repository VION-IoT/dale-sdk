using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE008 — [ServiceProperty] and [ServiceMeasuringPoint] must not use mutable collection
    ///     types such as <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, etc.
    ///     Use <c>ImmutableArray&lt;T&gt;</c> instead.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImmutableArrayServiceElementAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray);
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

            if (!IsRejectedArrayLike(property.Type))
            {
                return;
            }

            var attributeName = hasServiceProperty ? "ServiceProperty" : "ServiceMeasuringPoint";
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE008_ArrayMustBeImmutableArray,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       attributeName,
                                                       property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        /// <summary>
        ///     Returns true when the type is a mutable or non-ImmutableArray collection:
        ///     <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
        ///     <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>.
        /// </summary>
        private static bool IsRejectedArrayLike(ITypeSymbol type)
        {
            // T[] — array type
            if (type is IArrayTypeSymbol)
            {
                return true;
            }

            if (type is INamedTypeSymbol named && named.IsGenericType)
            {
                var ns = named.ContainingNamespace?.ToDisplayString();
                if (ns != "System.Collections.Generic")
                {
                    return false;
                }

                switch (named.OriginalDefinition.Name)
                {
                    case "List":
                    case "IList":
                    case "ICollection":
                    case "IEnumerable":
                    case "IReadOnlyList":
                    case "IReadOnlyCollection":
                        return true;
                }
            }

            return false;
        }
    }
}