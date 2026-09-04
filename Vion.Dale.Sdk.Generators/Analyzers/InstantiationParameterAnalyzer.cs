using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Vion.Dale.Sdk.Generators.Predicates;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE044 — enforces the discipline of <c>[InstantiationParameter]</c>. A
    ///     parameter must be declared on the logic-block class (not a component), paired with
    ///     <c>[ServiceProperty]</c>, a discrete scalar (bool / enum / integer / string), never
    ///     <c>WriteOnly</c>, an auto-property (no computed getter), and must not be re-declared on an
    ///     <c>override</c>/<c>new</c> member. A best-effort operation pass also flags an assignment to a
    ///     parameter inside the declaring block's own code outside the constructor / object-initializer
    ///     (<c>{ get; init; }</c> makes the compiler enforce this globally — the analyzer backstops plain
    ///     setters). Predicate <b>type</b> mismatches referencing a parameter are reported by
    ///     <see cref="IncludedWhenPredicateAnalyzer" /> (also DALE044).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InstantiationParameterAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE044_InstantiationParameterDiscipline);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
            context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment);
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Class)
            {
                return;
            }

            // Own members only — a parameter is reported at its declaration; inherited params are checked
            // when the declaring (base) type is analyzed.
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                var parameter = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.InstantiationParameterAttribute);
                if (parameter is null || property.DeclaringSyntaxReferences.IsEmpty)
                {
                    continue;
                }

                ValidateParameter(context, property, parameter);
            }
        }

        private static void ValidateParameter(SymbolAnalysisContext context, IPropertySymbol property, AttributeData parameter)
        {
            var location = AttributeLocation(parameter, property);

            // Placement: a parameter lives on the logic-block class itself (root-service scalar), never a component.
            if (!AnalyzerHelper.InheritsFromLogicBlockBase(property.ContainingType))
            {
                Report(context, location, property.Name, "[InstantiationParameter] must be declared on the logic-block class (a root-service scalar), not on a component type.");
                return;
            }

            // A non-public parameter is configured by nothing — the binders walk public instance
            // properties — and IncludedWhenPredicateAnalyzer builds its parameter map from the same walk,
            // so a gate naming one reports as unresolvable with nothing saying why. Reported as a warning
            // rather than an error: the declaration is inert today, so nothing that compiles now breaks.
            // Returning is the point: every rule below reports the same id at the same span, and one
            // .editorconfig entry moves all of them, so a private parameter of a refused type would draw
            // one Warning and one Error at one squiggle. Accessibility is the first thing to fix; the rest
            // are asked once the declaration is public.
            if (property.DeclaredAccessibility != Accessibility.Public)
            {
                ReportWarning(context,
                              location,
                              property.Name,
                              "[InstantiationParameter] must be a public property — this one is declared " + AccessibilityKeyword(property.DeclaredAccessibility) +
                              ", and the binders read a block's parameters from its public instance properties, so a non-public one is " +
                              "configured by nothing and no [IncludedWhen] gate can resolve to it.");
                return;
            }

            var serviceProperty = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
            if (serviceProperty is null)
            {
                Report(context, location, property.Name, "[InstantiationParameter] must be paired with [ServiceProperty] (it is a modifier on a real service property).");
            }
            else if (AnalyzerHelper.GetNamedArgument<bool>(serviceProperty, "WriteOnly"))
            {
                Report(context, location, property.Name, "[InstantiationParameter] cannot be combined with WriteOnly — a secret must not be an editor-visible structural driver.");
            }

            // [Persistent(Exclude = true)] asks for exactly what a parameter already gets, so it is a correct
            // declaration rather than a contradiction — and the message below would give it a reason that is
            // false for it. Only the opt-IN is refused. (Reading the named argument mirrors the WriteOnly rule
            // above, which reads its own.)
            var persistent = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.PersistentAttribute);
            if (persistent is not null && !AnalyzerHelper.GetNamedArgument<bool>(persistent, "Exclude"))
            {
                Report(context,
                       location,
                       property.Name,
                       "[InstantiationParameter] cannot be combined with [Persistent] — the configuration channel is a parameter's only source of truth, and a restored value would " +
                       "overwrite the configured one after the inclusion gates had already resolved against it. Persistence skips parameters, so the [Persistent] would do nothing.");
            }

            if (AnalyzerHelper.Categorize(property.Type) is RefCategory.Double or RefCategory.Other)
            {
                Report(context,
                       location,
                       property.Name,
                       "[InstantiationParameter] must be a discrete scalar — bool, enum, an integer kind, or string (never double/float, structs, or arrays).");
            }

            if (HasComputedGetter(property, context.CancellationToken))
            {
                Report(context, location, property.Name, "[InstantiationParameter] must be an auto-property — a computed getter breaks read-back honesty.");
            }

            if (HasBaseDeclaration(property))
            {
                Report(context,
                       location,
                       property.Name,
                       "re-declaring [InstantiationParameter] on an override/new member is not supported — declare it once, at the base declaration the hierarchy shares.");
            }
        }

        private static void AnalyzeAssignment(OperationAnalysisContext context)
        {
            var assignment = (IAssignmentOperation)context.Operation;
            if (assignment.Target is not IPropertyReferenceOperation propertyReference ||
                !AnalyzerHelper.HasAttribute(propertyReference.Property, AnalyzerHelper.InstantiationParameterAttribute))
            {
                return;
            }

            // Intra-type, best-effort: only police assignments inside the declaring block's own code.
            if (!SymbolEqualityComparer.Default.Equals(propertyReference.Property.ContainingType, context.ContainingSymbol?.ContainingType))
            {
                return;
            }

            // Allowed: the constructor, or an object/collection initializer.
            if (context.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } || assignment.Parent is IObjectOrCollectionInitializerOperation)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE044_InstantiationParameterDiscipline,
                                                       assignment.Syntax.GetLocation(),
                                                       propertyReference.Property.Name,
                                                       "an [InstantiationParameter] must not be assigned outside the constructor / object-initializer — its value is applied by the platform before Configure and is immutable at runtime."));
        }

        private static bool HasComputedGetter(IPropertySymbol property, CancellationToken cancellationToken)
        {
            foreach (var reference in property.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax declaration && GetComputedGetterBody(declaration) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private static SyntaxNode? GetComputedGetterBody(PropertyDeclarationSyntax declaration)
        {
            if (declaration.ExpressionBody != null)
            {
                return declaration.ExpressionBody.Expression;
            }

            var getter = declaration.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter is null)
            {
                return null;
            }

            return (SyntaxNode?)getter.ExpressionBody?.Expression ?? getter.Body;
        }

        private static bool HasBaseDeclaration(IPropertySymbol property)
        {
            for (var baseProperty = property.OverriddenProperty; baseProperty is not null; baseProperty = baseProperty.OverriddenProperty)
            {
                if (CarriesGateOrParameter(baseProperty))
                {
                    return true;
                }
            }

            for (var baseType = property.ContainingType?.BaseType; baseType is not null && baseType.SpecialType != SpecialType.System_Object; baseType = baseType.BaseType)
            {
                foreach (var shadowed in baseType.GetMembers(property.Name).OfType<IPropertySymbol>())
                {
                    if (CarriesGateOrParameter(shadowed))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CarriesGateOrParameter(IPropertySymbol property)
        {
            return AnalyzerHelper.HasAttribute(property, AnalyzerHelper.InstantiationParameterAttribute) ||
                   AnalyzerHelper.HasAttribute(property, AnalyzerHelper.IncludedWhenAttribute);
        }

        /// <summary>
        ///     The accessibility as the author wrote it. <see cref="Accessibility" />'s own names are the
        ///     compiler's (<c>ProtectedOrInternal</c>), and a message that named those would tell an author
        ///     about a keyword pair they never typed.
        /// </summary>
        private static string AccessibilityKeyword(Accessibility accessibility)
        {
            return accessibility switch
            {
                Accessibility.Private => "private",
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.Protected => "protected",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                _ => accessibility.ToString(),
            };
        }

        private static Location AttributeLocation(AttributeData attribute, ISymbol fallback)
        {
            return attribute.ApplicationSyntaxReference?.GetSyntax()?.GetLocation() ?? fallback.Locations.FirstOrDefault() ?? Location.None;
        }

        private static void Report(SymbolAnalysisContext context, Location location, string name, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE044_InstantiationParameterDiscipline, location, name, message));
        }

        /// <summary>
        ///     The advisory half of DALE044, through the <c>effectiveSeverity</c> overload — one descriptor,
        ///     two severities, as DALE045 does. Configuring or suppressing the id in <c>.editorconfig</c>
        ///     moves both together.
        /// </summary>
        private static void ReportWarning(SymbolAnalysisContext context, Location location, string name, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE044_InstantiationParameterDiscipline,
                                                       location,
                                                       DiagnosticSeverity.Warning,
                                                       null,
                                                       null,
                                                       name,
                                                       message));
        }
    }
}