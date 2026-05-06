using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE001 — Properties typed as service provider contract types must have a setter.
    ///     Mirrors DeclarativeContractBinder.IsContractType() which checks the property's type
    ///     (not the property attribute) for [ServiceProviderContractType].
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ContractPropertyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter);
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

            // Check if property type is a service provider contract type
            if (!AnalyzerHelper.IsServiceProviderContractType(property.Type))
            {
                return;
            }

            // Must have at least a private setter
            if (property.SetMethod != null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter, property.Locations.FirstOrDefault(), property.Name));
        }
    }
}
