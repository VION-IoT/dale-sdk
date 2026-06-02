using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

            // Interface members and abstract properties can't carry an initializer — adding one is a
            // compile error. The init obligation (and the actionable DALE018) belongs to the concrete
            // implementation, where this analyzer keeps firing. Without this guard, a [ServiceProperty]
            // ImmutableArray<T> declared on a [ServiceInterface] produces an unactionable false positive.
            if (property.ContainingType.TypeKind == TypeKind.Interface || property.IsAbstract)
            {
                return;
            }

            // DALE018 only applies to auto-implemented properties: the compiler-generated backing field behind an
            // auto `get;` defaults to default(ImmutableArray<T>) — which throws on access — unless the property
            // carries an initializer. A property with an explicit getter (an expression body `=> ...`, or a get
            // accessor with a body) is opaque: the analyzer can't prove it returns default, and the developer owns
            // what it returns (e.g. an initialized backing field with setter coercion). Such properties also can't
            // carry a property-level initializer, so checking only for that produced a false positive — exempt them.
            foreach (var syntaxRef in property.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax propDecl)
                {
                    continue;
                }

                // Property-level initializer (legal only on auto-properties) → safe.
                if (propDecl.Initializer != null)
                {
                    return;
                }

                // Expression-bodied property (`public ... Plan => ...;`) → explicit getter → exempt.
                if (propDecl.ExpressionBody != null)
                {
                    return;
                }

                // Explicit get accessor (`get => ...;` or `get { ... }`) → exempt. Only a bodyless `get;` reads
                // the default-backed field that can throw.
                var getter = propDecl.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter != null && (getter.Body != null || getter.ExpressionBody != null))
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