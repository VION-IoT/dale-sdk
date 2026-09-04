using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE048 — a <c>[ServiceProviderContractType]</c> token that is empty or whitespace. The
    ///     attribute assigns the token with no validation, and the token is the stable cloud-facing
    ///     identifier the introspection document carries, so a blank one reaches every reader downstream
    ///     with nothing having objected.
    ///     <para>
    ///         Uniqueness across the compilation is a separate, whole-compilation question — it needs the
    ///         <c>CompilationEnd</c> tag and a decision about the current-assembly boundary — and is not
    ///         this analyzer's.
    ///     </para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ContractTypeTokenAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE048_ContractTypeTokenMustNotBeBlank);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            var attribute = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.ServiceProviderContractTypeAttribute);
            if (attribute is null || attribute.ConstructorArguments.Length == 0)
            {
                return;
            }

            // An unset argument is a compile error of its own; only a value the author wrote is judged.
            if (attribute.ConstructorArguments[0].Value is not string token || !string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations.FirstOrDefault();
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE048_ContractTypeTokenMustNotBeBlank, location, type.Name));
        }
    }
}