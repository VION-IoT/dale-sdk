using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE034 — <c>MinChange</c> (the deadband) on a <c>[ServiceProperty]</c> /
    ///     <c>[ServiceMeasuringPoint]</c> whose value type has no resolvable
    ///     <c>IChangeThreshold&lt;T&gt;</c>. Built-ins exist for double/float/decimal/int/long/TimeSpan;
    ///     any other type needs an <c>IChangeThreshold&lt;ThatType&gt;</c> implementation visible in the
    ///     compilation. <c>bool</c> (no magnitude) is always an error.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MinChangeWithoutChangeThresholdAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Collect the resolvable custom-threshold types once per compilation (a scan across the source
            // and referenced assemblies), then reuse it for every property — instead of re-scanning per
            // property symbol.
            context.RegisterCompilationStartAction(start =>
                                                   {
                                                       var ichangeThreshold = start.Compilation.GetTypeByMetadataName(EmissionAttributeHelper.IChangeThresholdMetadataName);
                                                       if (ichangeThreshold == null)
                                                       {
                                                           // The SDK isn't referenced (no [ServiceProperty] can exist) — nothing to analyze.
                                                           return;
                                                       }

                                                       var customThresholdTypes = EmissionAttributeHelper.CollectCustomChangeThresholdTypes(start.Compilation, ichangeThreshold);
                                                       start.RegisterSymbolAction(symbolContext => AnalyzeProperty(symbolContext, customThresholdTypes), SymbolKind.Property);
                                                   });
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context, ImmutableHashSet<ITypeSymbol> customThresholdTypes)
        {
            var property = (IPropertySymbol)context.Symbol;

            var attribute = EmissionAttributeHelper.GetEmissionAttribute(property);
            if (attribute == null)
            {
                return;
            }

            var minChange = EmissionAttributeHelper.GetMinChange(attribute);
            if (minChange == null)
            {
                return;
            }

            // The runtime keys the registry on the member's value type. Unwrap Nullable<T> so the
            // author's intent (a double deadband on a double? property) is honoured — the underlying
            // type is what carries the magnitude.
            var valueType = EmissionAttributeHelper.Unwrap(property.Type);

            if (EmissionAttributeHelper.IsBuiltInThresholdType(valueType))
            {
                return;
            }

            if (customThresholdTypes.Contains(valueType))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE034_MinChangeWithoutChangeThreshold,
                                                       property.Locations.FirstOrDefault(),
                                                       property.Name,
                                                       valueType.ToDisplayString()));
        }
    }
}