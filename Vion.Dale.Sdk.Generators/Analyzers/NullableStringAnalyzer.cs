using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE017 — A [ServiceProperty] or [ServiceMeasuringPoint] typed <c>string</c> in a
    ///     nullable-disabled context is ambiguous (could be nullable or non-null). Enable nullable
    ///     annotations or use <c>string?</c> explicitly when null is intended.
    ///     In a nullable-enabled context, both <c>string</c> and <c>string?</c> are valid.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NullableStringAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE017_StringMustBeExplicitlyNullable);
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

            if (property.Type.SpecialType != SpecialType.System_String)
            {
                return;
            }

            // NullableAnnotation.None means the file/project has nullable context disabled —
            // the compiler cannot determine nullability intent. Fire DALE017 to require explicitness.
            if (property.NullableAnnotation != NullableAnnotation.None)
            {
                return;
            }

            var attributeName = hasServiceProperty ? "ServiceProperty" : "ServiceMeasuringPoint";
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE017_StringMustBeExplicitlyNullable, property.Locations.FirstOrDefault(), property.Name, attributeName));
        }
    }
}