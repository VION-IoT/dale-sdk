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
    ///     DALE031 — a computed observable property whose getter reads a <em>member</em> of a struct value the type
    ///     holds (e.g. <c>Bands.Capacity</c>, or <c>_stored.ActivePowerTotalKw</c>). The
    ///     Metalama.Patterns.Observability aspect tracks whole-value changes and method calls, but NOT direct
    ///     struct-member reads — so the computed property is woven without a dependency on that value and silently
    ///     never re-publishes when it changes.
    ///     <para>
    ///         The root does not matter: a probe against a real Metalama compilation (VION-81, kept as
    ///         <c>Vion.Dale.Sdk.Test/Core/MetalamaStructMemberDependencyReproShould.cs</c>) showed all three roots
    ///         — an observable property, an unmarked property, and a private field — are woven as dependency roots
    ///         and all three drop the member read. Whole-value reads (<c>=> Plan</c>, <c>=> _stored</c>) and
    ///         method calls (<c>Bands.Sum()</c>) ARE tracked, so they are deliberately not flagged.
    ///     </para>
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

            // One diagnostic per (computed property, struct-valued member) dependency, even if read several times.
            var reported = new HashSet<string>();

            foreach (var node in getterBody.DescendantNodesAndSelf())
            {
                if (!TryGetStructMemberRead(node, out var instance, out var memberNode, out var location))
                {
                    continue;
                }

                // `nameof(Bands.X)` is a compile-time constant, not a value read — it creates no dependency.
                if (IsInsideNameof(node))
                {
                    continue;
                }

                // The accessed member must be a field/property READ of the struct value. A method call
                // (`Bands.Sum()`) resolves to a method symbol here — the aspect tracks those, so skip them.
                var memberSymbol = context.SemanticModel.GetSymbolInfo(memberNode!, context.CancellationToken).Symbol;
                if (memberSymbol is not IFieldSymbol && memberSymbol is not IPropertySymbol)
                {
                    continue;
                }

                // Only `this`-relative access (`Bands.X` / `this.Bands.X` / `_stored.X`) is the intra-type trap.
                // A member read through a local or another object isn't tracked by this type's aspect regardless.
                if (!IsThisRelative(instance!))
                {
                    continue;
                }

                var instanceSymbol = context.SemanticModel.GetSymbolInfo(instance!, context.CancellationToken).Symbol;
                if (!TryGetTrackableInstanceType(instanceSymbol, property.ContainingType, out var instanceType))
                {
                    continue;
                }

                // Only struct values lose the dependency. The aspect tracks child objects, so a member read
                // off a reference-typed instance is fine.
                if (!instanceType!.IsValueType)
                {
                    continue;
                }

                if (!reported.Add(instanceSymbol!.Name))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked,
                                                           location!.GetLocation(),
                                                           property.Name,
                                                           instanceSymbol.Name,
                                                           memberSymbol.Name));
            }
        }

        // The two syntax shapes that read a member off an instance: `X.Member` and — for a nullable struct —
        // `X?.Member`, which is a ConditionalAccessExpression and not a MemberAccessExpression at all. The
        // nullable form is the one VION-81 was filed against, so it cannot be left to the first shape.
        private static bool TryGetStructMemberRead(SyntaxNode node, out ExpressionSyntax? instance, out SyntaxNode? memberNode, out SyntaxNode? location)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    instance = memberAccess.Expression;
                    memberNode = memberAccess;
                    location = memberAccess;
                    return true;

                case ConditionalAccessExpressionSyntax conditional:
                    // `_stored?.A.B` binds the whole chain; the first binding is the read off the struct.
                    var binding = conditional.WhenNotNull.DescendantNodesAndSelf().OfType<MemberBindingExpressionSyntax>().FirstOrDefault();
                    if (binding is null)
                    {
                        break;
                    }

                    instance = conditional.Expression;
                    memberNode = binding;
                    location = conditional;
                    return true;
            }

            instance = null;
            memberNode = null;
            location = null;
            return false;
        }

        // The instance whose member is read must be state this type owns and can reassign. VION-81 established
        // that the aspect tracks a private field as a dependency root exactly as it tracks a property — and drops
        // the member read off either — so both kinds qualify, observable-marked or not.
        private static bool TryGetTrackableInstanceType(ISymbol? instanceSymbol, INamedTypeSymbol containingType, out ITypeSymbol? instanceType)
        {
            instanceType = null;

            // A static is outside the instance's dependency graph entirely; flagging it is noise, not a finding.
            if (instanceSymbol is null || instanceSymbol.IsStatic)
            {
                return false;
            }

            switch (instanceSymbol)
            {
                case IPropertySymbol instanceProperty:
                    // A get-only property is assigned in the constructor and never again, so what it feeds can
                    // never go stale. Same reasoning as the readonly field below — keep the two in step.
                    if (instanceProperty.SetMethod is null)
                    {
                        return false;
                    }

                    instanceType = instanceProperty.Type;
                    return true;

                case IFieldSymbol instanceField:
                    // A field declared by a base type is not one this type's aspect weaves: Metalama reports it
                    // itself as LAMA5164 ("only private instance fields of the current type … are supported"),
                    // so DALE031 here would be duplicate noise on a shape the author is already being told about.
                    if (!SymbolEqualityComparer.Default.Equals(instanceField.ContainingType, containingType))
                    {
                        return false;
                    }

                    // A readonly field cannot be reassigned after construction, so the computed property it
                    // feeds can never go stale. Warning about it would be pure noise in consumer builds.
                    if (instanceField.IsReadOnly)
                    {
                        return false;
                    }

                    instanceType = instanceField.Type;
                    return true;

                default:
                    return false;
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
            return instance is IdentifierNameSyntax || (instance is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax);
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