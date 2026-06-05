using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE029 — Property setter using C# 13 'field' keyword on a LogicBlockBase subclass.
    ///     Metalama's [Observable] aspect (auto-applied via MetalamaTransitiveFabric) silently
    ///     drops the setter body at runtime. See DaleDiagnostics.DALE029 for details.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MetalamaFieldKeywordAnalyzer : DiagnosticAnalyzer
    {
        private const string LogicBlockBaseFullName = "Vion.Dale.Sdk.Core.LogicBlockBase";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE029_MetalamaFieldKeywordSetter);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeSetAccessor, SyntaxKind.SetAccessorDeclaration);
        }

        private static void AnalyzeSetAccessor(SyntaxNodeAnalysisContext context)
        {
            var accessor = (AccessorDeclarationSyntax)context.Node;

            // Auto-property setter — no body to drop. The aspect handles these correctly.
            var body = accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody;
            if (body is null)
            {
                return;
            }

            // Containing property
            var propertyDeclaration = accessor.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            if (propertyDeclaration is null)
            {
                return;
            }

            // Need the property symbol to (a) confirm the containing type derives from LogicBlockBase
            // and (b) verify identifiers named "field" inside the body actually resolve to the
            // synthesized backing field (avoiding false positives on user-defined locals/params).
            var propertySymbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
            if (propertySymbol is null)
            {
                return;
            }

            if (!DerivesFromLogicBlockBase(propertySymbol.ContainingType))
            {
                return;
            }

            if (!BodyReferencesFieldKeyword(body))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE029_MetalamaFieldKeywordSetter, propertyDeclaration.Identifier.GetLocation(), propertySymbol.Name));
        }

        private static bool DerivesFromLogicBlockBase(INamedTypeSymbol? type)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                if (AnalyzerHelper.GetFullName(t) == LogicBlockBaseFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BodyReferencesFieldKeyword(SyntaxNode body)
        {
            // The C# 13 contextual 'field' keyword surfaces in Roslyn 5.0 as a dedicated
            // FieldKeyword token (inside a FieldExpression node) — NOT as an IdentifierNameSyntax
            // with text "field". Matching the token kind is precise and avoids false positives
            // on user-defined locals/parameters named "field" (which would be IdentifierTokens).
            foreach (var token in body.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.FieldKeyword))
                {
                    return true;
                }
            }

            return false;
        }
    }
}