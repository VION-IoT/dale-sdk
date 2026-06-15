using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE033 — <c>StringFormat</c> on <c>[ServiceProperty]</c> / <c>[ServiceMeasuringPoint]</c> is
    ///     consumed only for <c>string</c> / <c>string?</c> members, and its value must not be a reserved
    ///     type-kind format (<c>date-time</c> / <c>duration</c> / <c>uuid</c>) — those have dedicated CLR
    ///     types. Open vocabulary otherwise: any other value is allowed (verbatim fallback in consumers).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringFormatOnNonStringAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> ReservedTypeKindFormats = new() { "date-time", "duration", "uuid" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE033_StringFormatOnNonString);
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

            var attr = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute) ??
                       AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
            if (attr is null)
            {
                return;
            }

            var format = AnalyzerHelper.GetNamedArgument<string>(attr, "StringFormat");
            if (string.IsNullOrEmpty(format))
            {
                return;
            }

            // Misplaced when the member isn't a string, or when a string member uses a value reserved
            // for a CLR type-kind (date-time/duration/uuid). Everything else is open vocabulary.
            var misplaced = property.Type.SpecialType != SpecialType.System_String || ReservedTypeKindFormats.Contains(format!);
            if (misplaced)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE033_StringFormatOnNonString,
                                                           property.Locations.FirstOrDefault(),
                                                           property.Name,
                                                           property.Type.ToDisplayString()));
            }
        }
    }
}
