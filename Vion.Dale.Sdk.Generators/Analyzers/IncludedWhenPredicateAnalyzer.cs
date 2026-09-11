using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Vion.Dale.Sdk.Generators.Predicates;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE043 / DALE044 — validates <c>[IncludedWhen("...")]</c> inclusion gates.
    ///     <para />
    ///     Registered on the logic-block <see cref="INamedTypeSymbol" />. For each gated member it enforces
    ///     the placement matrix (only a property-based interface binding, a contract binding, or a
    ///     service-bearing component is gateable — never a scalar service member, a <c>[Timer]</c> method,
    ///     a class-implemented interface, or the block class itself), parses the predicate, rejects
    ///     qualified references and re-gated <c>override</c>/<c>new</c> members, resolves bare references
    ///     against the block's <c>[InstantiationParameter]</c> properties (own + base), and type-checks
    ///     them. The analyzer <b>never evaluates</b> — strict-profile evaluation is the runtime's job.
    ///     <para>
    ///         The gateable test resolves a contract interface <b>both ways</b>, as
    ///         <see href="../../docs/sdk-surface-conventions.md">sdk-surface-conventions</see> § 5 requires of
    ///         every analyzer that keys off one — see <c>TypeImplementsLogicInterface</c>.
    ///     </para>
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

            // The by-name half of the gateable test needs every [LogicBlockContract] in the compilation,
            // which a per-symbol action cannot see on its own. Collected once, then closed over.
            context.RegisterCompilationStartAction(start =>
                                                   {
                                                       var contractInterfaceNames = CollectContractInterfaceNames(start.Compilation);
                                                       start.RegisterSymbolAction(symbol => AnalyzeType(symbol, contractInterfaceNames), SymbolKind.NamedType);
                                                   });
        }

        // The interface names LogicClassGenerator will emit for this compilation's OWN contracts — a
        // [LogicBlockContract]'s two role strings. A contract in a referenced assembly needs no entry: its
        // generated interfaces are already in metadata, where the by-symbol half reaches them.
        private static HashSet<string> CollectContractInterfaceNames(Compilation compilation)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var type in AnalyzerHelper.EnumerateDeclaredTypes(compilation.Assembly))
            {
                var contract = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.LogicBlockContractAttribute);
                if (contract is null)
                {
                    continue;
                }

                foreach (var role in new[]
                                     {
                                         AnalyzerHelper.GetNamedArgument<string>(contract, "BetweenInterface"),
                                         AnalyzerHelper.GetNamedArgument<string>(contract, "AndInterface"),
                                     })
                {
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        names.Add(role!);
                    }
                }
            }

            return names;
        }

        private static void AnalyzeType(SymbolAnalysisContext context, HashSet<string> contractInterfaceNames)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind == TypeKind.Class && AnalyzerHelper.InheritsFromLogicBlockBase(type))
            {
                AnalyzeBlock(context, type, contractInterfaceNames);
                return;
            }

            ReportMisplacedGates(context, type);
        }

        // A gate declared anywhere but on a logic block's own member is inert: all three binders read
        // [IncludedWhen] off the block type's properties and nothing else, so a component's own gate has no
        // reader. Reported here rather than left silent, mirroring the DALE044 that InstantiationParameterAnalyzer
        // reports for the same misplacement of the paired attribute.
        private static void ReportMisplacedGates(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            const string detail = "[IncludedWhen] is only valid on a member of a logic-block class — the binders read a gate off the block's own properties and " +
                                  "nowhere else, so a gate declared here is never evaluated. Gate the logic-block property that holds this type instead.";

            var typeGate = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.IncludedWhenAttribute);
            if (typeGate is not null && !type.DeclaringSyntaxReferences.IsEmpty)
            {
                ReportGate(context, GateLocation(typeGate, type), type.Name, PredicateOf(typeGate), detail);
            }

            // Own members only — an inherited gate is reported once, at the declaration that carries it.
            foreach (var member in type.GetMembers())
            {
                var gate = AnalyzerHelper.GetAttribute(member, AnalyzerHelper.IncludedWhenAttribute);
                if (gate is not null && !member.DeclaringSyntaxReferences.IsEmpty)
                {
                    ReportGate(context, GateLocation(gate, member), member.Name, PredicateOf(gate), detail);
                }
            }
        }

        private static void AnalyzeBlock(SymbolAnalysisContext context, INamedTypeSymbol block, HashSet<string> contractInterfaceNames)
        {
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

                ValidateProperty(context, property, gate, parameters, contractInterfaceNames);
            }
        }

        private static void ValidateProperty(SymbolAnalysisContext context,
                                             IPropertySymbol property,
                                             AttributeData gate,
                                             IReadOnlyDictionary<string, PredicateMember> parameters,
                                             HashSet<string> contractInterfaceNames)
        {
            var predicate = PredicateOf(gate);
            var location = GateLocation(gate, property);

            if (!IsGateable(property, contractInterfaceNames, context.CancellationToken))
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

        private static bool IsGateable(IPropertySymbol property, HashSet<string> contractInterfaceNames, CancellationToken cancellationToken)
        {
            // Contract binding (constructed by the binder → null when excluded).
            if (AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceProviderContractBindingAttribute) || AnalyzerHelper.IsServiceProviderContractType(property.Type))
            {
                return true;
            }

            // Property-based interface binding (explicit attribute, or the property type implements a [LogicInterface]).
            if (AnalyzerHelper.HasAttribute(property, AnalyzerHelper.LogicBlockInterfaceBindingAttribute) ||
                TypeImplementsLogicInterface(property.Type, contractInterfaceNames, cancellationToken))
            {
                return true;
            }

            // Service-bearing component (its whole service is gated).
            return property.Type is INamedTypeSymbol component && AnalyzerHelper.TypeHasServiceMembers(component);
        }

        /// <summary>
        ///     Whether <paramref name="type" /> implements a contract interface. Two lookups, because neither
        ///     alone is complete — the obligation
        ///     <see href="../../docs/sdk-surface-conventions.md">sdk-surface-conventions</see> § 5 places on
        ///     every analyzer keying off a contract interface, mirroring
        ///     <see cref="ServiceRelationAnalyzer" />'s <c>RelationBearingInterfaces</c>.
        ///     <list type="bullet">
        ///         <item>
        ///             <b>By symbol</b> — <c>[LogicInterface]</c> through <see cref="ITypeSymbol.AllInterfaces" />.
        ///             The accurate path, and the only one that reaches a contract in a <i>referenced</i> assembly.
        ///         </item>
        ///         <item>
        ///             <b>By name</b> — declared base lists against this compilation's own contract role names.
        ///             Necessary because every logic-block project runs Metalama, in whose pipeline an interface
        ///             <c>LogicClassGenerator</c> emits is an error type absent from <c>AllInterfaces</c> — so
        ///             for the common same-library case the symbol path finds nothing and a legitimately gated
        ///             binding draws <c>DALE043</c>.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         Both halves are transitive, because <c>DeclarativeInterfaceBinder</c> binds on
        ///         <c>Type.GetInterfaces()</c> and that is: an endpoint inherited from a base class, or reached
        ///         through an interface that extends the generated one, binds exactly like a directly declared
        ///         one. Reading only the property type's own base list would refuse a gate the runtime then
        ///         binds — the same defect as the symbol-only lookup, one step further out.
        ///     </para>
        ///     <para>
        ///         And no wider than that, because <c>DALE043</c> is an error and over-acceptance is invisible:
        ///         only a name a <c>[LogicBlockContract]</c> here declares as a role counts, and one that
        ///         already resolved — to any ancestor, interface or base class — is skipped. The symbol half
        ///         ran first and found no <c>[LogicInterface]</c>, so a resolved ancestor of that name is an
        ///         ordinary type sharing a role's spelling, and the binder will not bind it.
        ///     </para>
        /// </summary>
        private static bool TypeImplementsLogicInterface(ITypeSymbol type, HashSet<string> contractInterfaceNames, CancellationToken cancellationToken)
        {
            if (type.AllInterfaces.Any(i => AnalyzerHelper.HasAttribute(i, AnalyzerHelper.LogicInterfaceAttribute)))
            {
                return true;
            }

            if (contractInterfaceNames.Count == 0 || type is not INamedTypeSymbol named)
            {
                return false;
            }

            // Lazily, and the role-name test first: IDE live analysis runs this on every keystroke, and a
            // base list naming a contract role at all is the rare case. Nothing below the first hit is walked.
            return AncestryDeclaringBaseTypes(named)
                   .SelectMany(ancestor => AnalyzerHelper.DeclaredBaseTypeNames(ancestor, cancellationToken))
                   .Where(contractInterfaceNames.Contains)
                   .Any(name => !ResolvesToAncestor(named, name));
        }

        // Whether <paramref name="name" /> already names an ancestor of <paramref name="type" /> that the
        // compiler resolved. TypeKind.Error is excluded deliberately: an unresolved interface still appears in
        // AllInterfaces when it is inherited through one that resolves, and that is precisely the name the
        // by-name half exists to find.
        private static bool ResolvesToAncestor(INamedTypeSymbol type, string name)
        {
            return AncestryDeclaringBaseTypes(type).Any(ancestor => ancestor.TypeKind != TypeKind.Error && ancestor.Name == name);
        }

        // Every type whose declared base list can carry the generated name: the property's type, the base
        // classes it inherits from, and the interfaces it implements. AllInterfaces is already the transitive
        // closure of the interfaces and the BaseType chain that of the base classes, so this needs no
        // recursion of its own. An error-type entry among them contributes nothing rather than needing a
        // guard: it has no DeclaringSyntaxReferences, so there is no base list to read off it.
        private static IEnumerable<INamedTypeSymbol> AncestryDeclaringBaseTypes(INamedTypeSymbol type)
        {
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                yield return current;
            }

            foreach (var iface in type.AllInterfaces)
            {
                yield return iface;
            }
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