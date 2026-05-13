using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE025 — A property declares both <c>[ServiceProperty]</c> and
    ///     <c>[ServiceMeasuringPoint]</c> and both set the same field (Title / Description /
    ///     Unit / Minimum / Maximum) to conflicting non-empty values.
    ///     <para />
    ///     The cross-fill rule merges these two attributes into a single schema and needs one
    ///     source of truth per field. When both attributes set the same field, the merge is
    ///     ambiguous — the warning prompts the author to pick one.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CrossFillConflictAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] StringFields = { "Title", "Description", "Unit" };

        private static readonly string[] NumericFields = { "Minimum", "Maximum" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE025_CrossFillConflict);
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
            var spAttr = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
            var mpAttr = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);

            if (spAttr == null || mpAttr == null)
            {
                return;
            }

            foreach (var field in StringFields)
            {
                var sp = GetStringNamedArg(spAttr, field);
                var mp = GetStringNamedArg(mpAttr, field);

                if (sp != null && mp != null && sp != mp)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE025_CrossFillConflict,
                                                               property.Locations.FirstOrDefault(),
                                                               property.Name,
                                                               field,
                                                               "\"" + sp + "\"",
                                                               "\"" + mp + "\""));
                }
            }

            foreach (var field in NumericFields)
            {
                var sp = GetDoubleNamedArg(spAttr, field);
                var mp = GetDoubleNamedArg(mpAttr, field);

                if (sp.HasValue && mp.HasValue && sp.Value != mp.Value)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE025_CrossFillConflict,
                                                               property.Locations.FirstOrDefault(),
                                                               property.Name,
                                                               field,
                                                               sp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                                               mp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        private static string? GetStringNamedArg(AttributeData attr, string name)
        {
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == name && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }

            return null;
        }

        private static double? GetDoubleNamedArg(AttributeData attr, string name)
        {
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == name && kvp.Value.Value is double d)
                {
                    return d;
                }
            }

            return null;
        }
    }
}
