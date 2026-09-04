using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE033 — <c>StringFormat</c> is honored only on <c>string</c> / <c>string?</c> members, and its
    ///     value must not be a reserved type-kind format. Judges all three sites that carry the knob:
    ///     <c>[ServiceProperty]</c>, <c>[ServiceMeasuringPoint]</c> and <c>[StructField]</c>.
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

            // A struct field carries the same knob (StructFieldAttribute.StringFormat), TypeRefBuilder
            // emits it into the schema, and nothing judged it. A positional record struct's members are
            // its primary-constructor parameters, which is why this is a parameter action.
            context.RegisterSymbolAction(AnalyzeStructField, SymbolKind.Parameter);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;

            // Every emission attribute the member declares, not whichever is found first: the two carry
            // independent policies for independent streams, so a dual-annotated member whose measuring
            // point held the misplaced hint was judged by nothing.
            foreach (var attribute in EmissionAttributeHelper.GetEmissionAttributes(property))
            {
                Judge(context, attribute, property.Type, property.Name, EmissionAttributeHelper.LocationOf(attribute, property));
            }
        }

        private static void AnalyzeStructField(SymbolAnalysisContext context)
        {
            var parameter = (IParameterSymbol)context.Symbol;

            var structField = AnalyzerHelper.GetAttribute(parameter, AnalyzerHelper.StructFieldAttribute);
            if (structField is null)
            {
                return;
            }

            var location = structField.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? parameter.Locations.FirstOrDefault();
            Judge(context, structField, parameter.Type, parameter.Name, location);
        }

        private static void Judge(SymbolAnalysisContext context, AttributeData attribute, ITypeSymbol memberType, string memberName, Location? location)
        {
            var format = AnalyzerHelper.GetNamedArgument<string>(attribute, "StringFormat");
            if (string.IsNullOrEmpty(format))
            {
                return;
            }

            // Misplaced when the member isn't a string, or when a string member uses a value reserved
            // for a CLR type-kind (date-time/duration/uuid). Everything else is open vocabulary.
            var misplaced = memberType.SpecialType != SpecialType.System_String || ReservedTypeKindFormats.Contains(format!);
            if (misplaced)
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE033_StringFormatOnNonString, location, memberName, memberType.ToDisplayString()));
            }
        }
    }
}