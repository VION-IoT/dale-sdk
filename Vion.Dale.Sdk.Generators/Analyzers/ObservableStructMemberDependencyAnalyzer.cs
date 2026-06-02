using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE031 — a computed observable property whose getter reads a <em>member</em> of a struct-typed
    ///     observable property (e.g. <c>Bands.Capacity</c> where <c>Bands</c> is a struct <c>[ServiceProperty]</c>).
    ///     The Metalama.Patterns.Observability aspect tracks whole-property changes and method calls on the struct
    ///     property, but NOT direct struct-member reads — so the computed property is woven without a dependency
    ///     on the struct property and silently never re-publishes when it changes. Method calls (<c>Bands.Sum()</c>)
    ///     ARE tracked by the aspect, so they are deliberately not flagged.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ObservableStructMemberDependencyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        }

        private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
        {
            var propDecl = (PropertyDeclarationSyntax)context.Node;

            // Only computed properties have a getter body to derive from. Auto-properties just read a backing
            // field — there's no cross-property dependency to mistrack.
            var getterBody = GetComputedGetterBody(propDecl);
            if (getterBody is null)
            {
                return;
            }

            if (context.SemanticModel.GetDeclaredSymbol(propDecl, context.CancellationToken) is not IPropertySymbol property)
            {
                return;
            }

            // The staleness only matters if the computed property is itself observed (published). Its presence
            // also means the containing type receives the [Observable] aspect (MetalamaSharedLogic.IsServiceType).
            if (!IsObservableServiceMember(property))
            {
                return;
            }

            // Interface default-impl getters aren't woven the way concrete blocks are; stay out of their way.
            if (property.ContainingType.TypeKind == TypeKind.Interface)
            {
                return;
            }

            // One diagnostic per (computed property, struct property) dependency, even if read several times.
            var reported = new HashSet<string>();

            foreach (var memberAccess in getterBody.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                // `nameof(Bands.X)` is a compile-time constant, not a value read — it creates no dependency.
                if (IsInsideNameof(memberAccess))
                {
                    continue;
                }

                // The accessed member must be a field/property READ of the struct value. A method call
                // (`Bands.Sum()`) resolves to a method symbol here — the aspect tracks those, so skip them.
                var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
                if (memberSymbol is not IFieldSymbol && memberSymbol is not IPropertySymbol)
                {
                    continue;
                }

                // Only `this`-relative access (`Bands.X` / `this.Bands.X`) is the intra-type trap. A member read
                // through another object isn't tracked by this type's aspect regardless.
                if (!IsThisRelative(memberAccess.Expression))
                {
                    continue;
                }

                // The instance must be an observable, struct-typed property of this type.
                if (context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol is not IPropertySymbol structProperty)
                {
                    continue;
                }

                if (!structProperty.Type.IsValueType || !IsObservableServiceMember(structProperty))
                {
                    continue;
                }

                if (!reported.Add(structProperty.Name))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked,
                    memberAccess.GetLocation(),
                    property.Name, structProperty.Name, memberSymbol.Name));
            }
        }

        private static bool IsObservableServiceMember(IPropertySymbol property)
        {
            return AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServicePropertyAttribute) ||
                   AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
        }

        // The getter body to analyze: the expression of an expression-bodied property (`=> ...`) or of an
        // expression-bodied get accessor (`get => ...;`), or the block of a `get { ... }`. Null for auto-getters.
        private static SyntaxNode? GetComputedGetterBody(PropertyDeclarationSyntax propDecl)
        {
            if (propDecl.ExpressionBody != null)
            {
                return propDecl.ExpressionBody.Expression;
            }

            var getter = propDecl.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter is null)
            {
                return null;
            }

            return (SyntaxNode?)getter.ExpressionBody?.Expression ?? getter.Body;
        }

        // `Bands` (implicit this) or `this.Bands`.
        private static bool IsThisRelative(ExpressionSyntax instance)
        {
            return instance is IdentifierNameSyntax
                   || (instance is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax);
        }

        // True when the node sits inside a nameof(...) operator, whose argument is evaluated at compile time
        // and therefore creates no runtime dependency. Walk is bounded to the enclosing accessor/property.
        private static bool IsInsideNameof(SyntaxNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier: { ValueText: "nameof" } } })
                {
                    return true;
                }

                if (current is AccessorDeclarationSyntax || current is PropertyDeclarationSyntax)
                {
                    break;
                }
            }

            return false;
        }
    }
}
