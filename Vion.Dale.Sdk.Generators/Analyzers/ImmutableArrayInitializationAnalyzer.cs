using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE018 — A [ServiceProperty] or [ServiceMeasuringPoint] typed <c>ImmutableArray&lt;T&gt;</c>
    ///     without an initializer defaults to <c>IsDefault == true</c> and throws on any access.
    ///     Initialise to <c>ImmutableArray&lt;T&gt;.Empty</c> or similar.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImmutableArrayInitializationAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE018_ImmutableArrayMustBeInitialised);
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

            if (!IsImmutableArray(property.Type))
            {
                return;
            }

            // Check syntax for an initializer on the property declaration.
            foreach (var syntaxRef in property.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax(context.CancellationToken);
                if (syntax is PropertyDeclarationSyntax propDecl && propDecl.Initializer != null)
                {
                    return;
                }
            }

            var attributeName = hasServiceProperty ? "ServiceProperty" : "ServiceMeasuringPoint";
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE018_ImmutableArrayMustBeInitialised, property.Locations.FirstOrDefault(), property.Name, attributeName));
        }

        private static bool IsImmutableArray(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named && named.Name == "ImmutableArray" && named.ContainingNamespace?.Name == "Immutable" && named.IsGenericType;
        }
    }
}