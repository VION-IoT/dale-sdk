using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Vion.Dale.Sdk.Generators.Predicates;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE043 / DALE044 — validates <c>[IncludedWhen("...")]</c> inclusion gates (RFC 0016).
    ///     <para />
    ///     Registered on the logic-block <see cref="INamedTypeSymbol" />. For each gated member it enforces
    ///     the §2.3 placement matrix (only a property-based interface binding, a contract binding, or a
    ///     service-bearing component is gateable — never a scalar service member, a <c>[Timer]</c> method,
    ///     a class-implemented interface, or the block class itself), parses the predicate, rejects
    ///     qualified references and re-gated <c>override</c>/<c>new</c> members, resolves bare references
    ///     against the block's <c>[InstantiationParameter]</c> properties (own + base), and type-checks
    ///     them. The analyzer <b>never evaluates</b> — strict-profile evaluation is the runtime's job.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IncludedWhenPredicateAnalyzer : DiagnosticAnalyzer
    {
        // Sentinel service id: bare refs resolve against it; no valid qualified-ref first segment can name
        // it (it is not a legal C# identifier), so every qualified reference fails resolution — inclusion
        // gates are bare-single-segment only.
        private const string ParameterServiceId = "instantiation-parameters";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE043_IncludedWhenInvalid, DaleDiagnostics.DALE044_InstantiationParameterDiscipline);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeBlock, SymbolKind.NamedType);
        }

        private static void AnalyzeBlock(SymbolAnalysisContext context)
        {
            var block = (INamedTypeSymbol)context.Symbol;
            if (block.TypeKind != TypeKind.Class || !AnalyzerHelper.InheritsFromLogicBlockBase(block))
            {
                return;
            }

            // [IncludedWhen] on the block class itself → not gateable (whole-block existence is the operator
            // adding the instance or not; a class-implemented interface has no member to carry the gate).
            var classGate = AnalyzerHelper.GetAttribute(block, AnalyzerHelper.IncludedWhenAttribute);
            if (classGate is not null)
            {
                ReportGate(context,
                           GateLocation(classGate, block),
                           block.Name,
                           PredicateOf(classGate),
                           "[IncludedWhen] is not valid on the block class — whole-block existence is decided by adding the instance; a class-implemented interface must be a property-based binding to be gateable.");
            }

            // [IncludedWhen] on a method (e.g. a [Timer]) → not gateable (timers are not in the definition view).
            foreach (var method in block.GetMembers().OfType<IMethodSymbol>())
            {
                var methodGate = AnalyzerHelper.GetAttribute(method, AnalyzerHelper.IncludedWhenAttribute);
                if (methodGate is not null && !method.DeclaringSyntaxReferences.IsEmpty)
                {
                    ReportGate(context,
                               GateLocation(methodGate, method),
                               method.Name,
                               PredicateOf(methodGate),
                               "[IncludedWhen] is not valid on a method — a [Timer] is not in the definition view; gate it in code instead (e.g. `if (Count < 3) return;`).");
                }
            }

            var parameters = CollectParameterMembers(block);
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var property in AnalyzerHelper.EnumerateProperties(block))
            {
                var gate = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.IncludedWhenAttribute);
                if (gate is null || property.DeclaringSyntaxReferences.IsEmpty)
                {
                    continue;
                }

                if (!seen.Add(property.Name)) // most-derived declaration wins
                {
                    continue;
                }

                ValidateProperty(context, property, gate, parameters);
            }
        }

        private static void ValidateProperty(SymbolAnalysisContext context, IPropertySymbol property, AttributeData gate, IReadOnlyDictionary<string, PredicateMember> parameters)
        {
            var predicate = PredicateOf(gate);
            var location = GateLocation(gate, property);

            if (!IsGateable(property))
            {
                ReportGate(context,
                           location,
                           property.Name,
                           predicate,
                           "this member is not gateable — only a property-based interface binding, a contract binding, or a service-bearing component can carry [IncludedWhen]. A scalar service property/measuring point keeps publishing; use [Presentation(VisibleWhen = ...)] for display relevance.");
                return;
            }

            if (HasBaseGateOrParameter(property))
            {
                ReportGate(context,
                           location,
                           property.Name,
                           predicate,
                           "re-declaring [IncludedWhen] on an override/new member is not supported — declare the gate once, at the base declaration the hierarchy shares.");
                return;
            }

            var parse = PredicateParser.Parse(predicate);
            if (!parse.IsValid)
            {
                if (parse.ErrorKind == PredicateErrorKind.ExpectedLiteral)
                {
                    ReportDiscipline(context, location, property.Name, $"[IncludedWhen] predicate \"{predicate}\" has a type error: {parse.Error}");
                }
                else
                {
                    ReportGate(context, location, property.Name, predicate, parse.Error!);
                }

                return;
            }

            var predicateContext = new PredicateContext(new Dictionary<string, PredicateService>(System.StringComparer.Ordinal) { [ParameterServiceId] = new(parameters) },
                                                        ParameterServiceId);

            foreach (var error in PredicateTypeChecker.Check(parse.Ast!, predicateContext))
            {
                if (error.IsTypeError)
                {
                    ReportDiscipline(context, location, property.Name, $"[IncludedWhen] predicate \"{predicate}\" has a type error: {error.Message}");
                }
                else
                {
                    ReportGate(context, location, property.Name, predicate, error.Message);
                }
            }
        }

        // The block's [InstantiationParameter] properties (own + base, most-derived wins) — the only
        // members an inclusion predicate may reference.
        private static Dictionary<string, PredicateMember> CollectParameterMembers(INamedTypeSymbol block)
        {
            var members = new Dictionary<string, PredicateMember>(System.StringComparer.Ordinal);
            foreach (var property in AnalyzerHelper.EnumerateProperties(block))
            {
                if (!AnalyzerHelper.HasAttribute(property, AnalyzerHelper.InstantiationParameterAttribute) || members.ContainsKey(property.Name))
                {
                    continue;
                }

                members[property.Name] = AnalyzerHelper.MakeMember(property.Type, AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute));
            }

            return members;
        }

        private static bool IsGateable(IPropertySymbol property)
        {
            // Contract binding (constructed by the binder → null when excluded).
            if (AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceProviderContractBindingAttribute) || AnalyzerHelper.IsServiceProviderContractType(property.Type))
            {
                return true;
            }

            // Property-based interface binding (explicit attribute, or the property type implements a [LogicInterface]).
            if (AnalyzerHelper.HasAttribute(property, AnalyzerHelper.LogicBlockInterfaceBindingAttribute) || TypeImplementsLogicInterface(property.Type))
            {
                return true;
            }

            // Service-bearing component (its whole service is gated).
            return property.Type is INamedTypeSymbol component && AnalyzerHelper.TypeHasServiceMembers(component);
        }

        private static bool TypeImplementsLogicInterface(ITypeSymbol type)
        {
            return type.AllInterfaces.Any(i => AnalyzerHelper.HasAttribute(i, AnalyzerHelper.LogicInterfaceAttribute));
        }

        private static bool HasBaseGateOrParameter(IPropertySymbol property)
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
            return AnalyzerHelper.HasAttribute(property, AnalyzerHelper.IncludedWhenAttribute) ||
                   AnalyzerHelper.HasAttribute(property, AnalyzerHelper.InstantiationParameterAttribute);
        }

        private static string PredicateOf(AttributeData gate)
        {
            return gate.ConstructorArguments.Length > 0 && gate.ConstructorArguments[0].Value is string predicate ? predicate : string.Empty;
        }

        private static Location GateLocation(AttributeData gate, ISymbol fallback)
        {
            var syntax = gate.ApplicationSyntaxReference?.GetSyntax();
            if (syntax is AttributeSyntax { ArgumentList.Arguments: { Count: > 0 } arguments })
            {
                return arguments[0].Expression.GetLocation();
            }

            return syntax?.GetLocation() ?? fallback.Locations.FirstOrDefault() ?? Location.None;
        }

        private static void ReportGate(SymbolAnalysisContext context, Location location, string name, string predicate, string detail)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE043_IncludedWhenInvalid, location, name, predicate, detail));
        }

        private static void ReportDiscipline(SymbolAnalysisContext context, Location location, string name, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE044_InstantiationParameterDiscipline, location, name, message));
        }
    }
}