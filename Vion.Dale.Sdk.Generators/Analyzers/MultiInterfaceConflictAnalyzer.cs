using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE020 — A LogicBlock implements two or more interfaces that declare a property with
    ///     the same name and conflicting <c>Unit</c> attribute values.
    ///     <para />
    ///     The cascade rule (interface attribute → class property) has no way to pick a winner
    ///     when two interface declarations disagree. Author resolves by writing an explicit
    ///     attribute on the class property to override, or by aligning the interface declarations.
    ///     <para />
    ///     Suppressed when the class declares its own <c>[ServiceProperty]</c> or
    ///     <c>[ServiceMeasuringPoint]</c> attribute on the property — that's the explicit
    ///     override the rule asks for.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MultiInterfaceConflictAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE020_MultiInterfaceConflict);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Class || type.IsAbstract)
            {
                return;
            }

            // Bucket interface property declarations by name across ALL implemented interfaces.
            var byName = new Dictionary<string, List<IPropertySymbol>>();

            foreach (var iface in type.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    var spOrMp = AnalyzerHelper.GetAttribute(member, AnalyzerHelper.ServicePropertyAttribute)
                              ?? AnalyzerHelper.GetAttribute(member, AnalyzerHelper.ServiceMeasuringPointAttribute);
                    if (spOrMp == null)
                    {
                        continue;
                    }

                    if (!byName.TryGetValue(member.Name, out var list))
                    {
                        list = new List<IPropertySymbol>();
                        byName[member.Name] = list;
                    }

                    list.Add(member);
                }
            }

            foreach (var kvp in byName)
            {
                if (kvp.Value.Count < 2)
                {
                    continue;
                }

                var units = new HashSet<string>();
                foreach (var ifaceProp in kvp.Value)
                {
                    var unit = GetUnit(ifaceProp);
                    if (unit != null)
                    {
                        units.Add(unit);
                    }
                }

                if (units.Count < 2)
                {
                    continue;
                }

                // Suppress if the class declares its own ServiceProperty / ServiceMeasuringPoint
                // attribute on the implementing property — the override resolves the conflict.
                var classProp = type.GetMembers(kvp.Key).OfType<IPropertySymbol>().FirstOrDefault();
                if (classProp != null && HasExplicitOverride(classProp))
                {
                    continue;
                }

                var unitList = string.Join(", ", units.OrderBy(u => u, System.StringComparer.Ordinal).Select(u => "\"" + u + "\""));
                var location = classProp?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault();

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE020_MultiInterfaceConflict,
                                                           location,
                                                           type.Name,
                                                           kvp.Key,
                                                           unitList));
            }
        }

        private static string? GetUnit(IPropertySymbol property)
        {
            var attr = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute)
                    ?? AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
            if (attr == null)
            {
                return null;
            }

            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == "Unit" && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }

            // Also walk preset-attribute constructor defaults (the preset class sets Unit in its ctor).
            // We can't easily resolve those via AttributeData — settle for syntactic NamedArguments.
            // Integrator presets that share a Unit aren't conflicting, so skipping them is fine.
            return null;
        }

        private static bool HasExplicitOverride(IPropertySymbol classProp)
        {
            return AnalyzerHelper.HasAttribute(classProp, AnalyzerHelper.ServicePropertyAttribute)
                || AnalyzerHelper.HasAttribute(classProp, AnalyzerHelper.ServiceMeasuringPointAttribute);
        }
    }
}
