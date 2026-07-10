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
    ///     DALE041 / DALE042 — validates <c>[Presentation(VisibleWhen = "...")]</c> predicates (RFC 0017).
    ///     <para />
    ///     Registered on the logic-block <see cref="INamedTypeSymbol" />. It reconstructs the same service
    ///     map the runtime <c>DeclarativeServiceBinder</c> builds — the <b>root service</b> (identified by
    ///     the block class name) with the block's own <c>[ServiceProperty]</c>/<c>[ServiceMeasuringPoint]</c>
    ///     members, plus one level of <b>component services</b> (each identified by its holding property's
    ///     name) for properties whose type carries service members. Each source-authored predicate is
    ///     parsed (grammar of <c>docs/predicates.md</c> §2.2), its references resolved against that map
    ///     (§3), and type-checked (§2.3). The analyzer <b>never evaluates</b> — strict-profile C#
    ///     evaluation is RFC 0016's job.
    /// </summary>
    [DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
    public sealed class VisibleWhenPredicateAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE041_VisibleWhenUnresolved, DaleDiagnostics.DALE042_VisibleWhenTypeMismatch);
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
            if (block.TypeKind != TypeKind.Class || block.IsAbstract || !InheritsFromLogicBlockBase(block))
            {
                return;
            }

            // Cheap early-out: only pay for the service map when some member actually carries a predicate.
            var annotated = CollectAnnotatedMembers(block);
            if (annotated.Count == 0)
            {
                return;
            }

            var services = BuildServiceMap(block);

            foreach (var member in annotated)
            {
                Validate(context, member, services);
            }
        }

        private static void Validate(SymbolAnalysisContext context, AnnotatedMember member, IReadOnlyDictionary<string, PredicateService> services)
        {
            var location = GetPredicateLocation(member);
            var parse = PredicateParser.Parse(member.Predicate);
            if (!parse.IsValid)
            {
                var descriptor = parse.ErrorKind == PredicateErrorKind.ExpectedLiteral
                                     ? DaleDiagnostics.DALE042_VisibleWhenTypeMismatch
                                     : DaleDiagnostics.DALE041_VisibleWhenUnresolved;
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, member.Symbol.Name, member.Predicate, parse.Error));
                return;
            }

            var predicateContext = new PredicateContext(services, member.OwnServiceId);
            var errors = PredicateTypeChecker.Check(parse.Ast!, predicateContext);
            foreach (var error in errors)
            {
                var descriptor = error.IsTypeError
                                     ? DaleDiagnostics.DALE042_VisibleWhenTypeMismatch
                                     : DaleDiagnostics.DALE041_VisibleWhenUnresolved;
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, member.Symbol.Name, member.Predicate, error.Message));
            }
        }

        // ── Annotated-member discovery ──

        private static List<AnnotatedMember> CollectAnnotatedMembers(INamedTypeSymbol block)
        {
            var result = new List<AnnotatedMember>();
            var seen = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);

            // Root service: the block's own service members, addressed by the class name.
            foreach (var property in EnumerateProperties(block))
            {
                TryAddAnnotated(property, block.Name, seen, result);
            }

            // One level of component services: a property whose type carries service members forms a
            // component service identified by the holding property's name.
            foreach (var holder in EnumerateProperties(block))
            {
                if (holder.Type is not INamedTypeSymbol componentType || !TypeHasServiceMembers(componentType))
                {
                    continue;
                }

                foreach (var property in EnumerateProperties(componentType))
                {
                    TryAddAnnotated(property, holder.Name, seen, result);
                }
            }

            return result;
        }

        private static void TryAddAnnotated(IPropertySymbol property, string ownServiceId, HashSet<IPropertySymbol> seen, List<AnnotatedMember> result)
        {
            // Only members declared in this compilation are validated here; metadata-declared predicates
            // were validated when their assembly was built.
            if (property.DeclaringSyntaxReferences.IsEmpty)
            {
                return;
            }

            var presentation = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.PresentationAttribute);
            if (presentation is null)
            {
                return;
            }

            var predicate = AnalyzerHelper.GetNamedArgument<string>(presentation, "VisibleWhen");
            if (string.IsNullOrWhiteSpace(predicate))
            {
                return;
            }

            // VisibleWhen is only meaningful on a service element (property or measuring point).
            if (!IsServiceElement(property))
            {
                return;
            }

            if (!seen.Add(property))
            {
                return;
            }

            result.Add(new AnnotatedMember(property, predicate!, ownServiceId, presentation));
        }

        // ── Service-map construction (mirrors DeclarativeServiceBinder) ──

        private static Dictionary<string, PredicateService> BuildServiceMap(INamedTypeSymbol block)
        {
            var services = new Dictionary<string, PredicateService>(System.StringComparer.Ordinal)
                           {
                               [block.Name] = new PredicateService(CollectServiceMembers(block)),
                           };

            foreach (var holder in EnumerateProperties(block))
            {
                if (holder.Type is not INamedTypeSymbol componentType || !TypeHasServiceMembers(componentType))
                {
                    continue;
                }

                // Property name is the service identifier (last binding wins, like the binder's dictionary).
                services[holder.Name] = new PredicateService(CollectServiceMembers(componentType));
            }

            return services;
        }

        private static Dictionary<string, PredicateMember> CollectServiceMembers(INamedTypeSymbol type)
        {
            var members = new Dictionary<string, PredicateMember>(System.StringComparer.Ordinal);

            // Directly declared (+ inherited) service members: most-derived declaration wins.
            foreach (var property in EnumerateProperties(type))
            {
                var sp = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
                var mp = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
                if (sp is null && mp is null)
                {
                    continue;
                }

                if (!members.ContainsKey(property.Name))
                {
                    members[property.Name] = MakeMember(property.Type, sp);
                }
            }

            // Service-interface members bound by name (the interface owns the schema contract).
            foreach (var iface in type.AllInterfaces)
            {
                if (!AnalyzerHelper.HasAttribute(iface, AnalyzerHelper.ServiceInterfaceAttribute))
                {
                    continue;
                }

                foreach (var property in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    var sp = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute);
                    var mp = AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
                    if (sp is null && mp is null)
                    {
                        continue;
                    }

                    if (!members.ContainsKey(property.Name) && TypeHasProperty(type, property.Name))
                    {
                        members[property.Name] = MakeMember(property.Type, sp);
                    }
                }
            }

            return members;
        }

        private static PredicateMember MakeMember(ITypeSymbol type, AttributeData? serviceProperty)
        {
            var isServiceProperty = serviceProperty is not null;
            var isWriteOnly = isServiceProperty && AnalyzerHelper.GetNamedArgument<bool>(serviceProperty!, "WriteOnly");
            return new PredicateMember(Categorize(type), isServiceProperty, isWriteOnly);
        }

        private static RefCategory Categorize(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return RefCategory.Enum;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return RefCategory.Bool;
                case SpecialType.System_String:
                    return RefCategory.String;
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return RefCategory.Double;
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return RefCategory.Integer;
                default:
                    return RefCategory.Other;
            }
        }

        // ── Symbol helpers ──

        private static bool IsServiceElement(IPropertySymbol property)
        {
            return AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServicePropertyAttribute) ||
                   AnalyzerHelper.HasAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
        }

        private static bool TypeHasServiceMembers(INamedTypeSymbol type)
        {
            foreach (var property in EnumerateProperties(type))
            {
                if (IsServiceElement(property))
                {
                    return true;
                }
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (!AnalyzerHelper.HasAttribute(iface, AnalyzerHelper.ServiceInterfaceAttribute))
                {
                    continue;
                }

                foreach (var property in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    if (IsServiceElement(property))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TypeHasProperty(INamedTypeSymbol type, string name)
        {
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (current.GetMembers(name).OfType<IPropertySymbol>().Any())
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<IPropertySymbol> EnumerateProperties(INamedTypeSymbol type)
        {
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    yield return property;
                }
            }
        }

        private static bool InheritsFromLogicBlockBase(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (AnalyzerHelper.GetFullName(current) == AnalyzerHelper.LogicBlockBaseType)
                {
                    return true;
                }
            }

            return false;
        }

        private static Location GetPredicateLocation(AnnotatedMember member)
        {
            var syntaxReference = member.Presentation.ApplicationSyntaxReference;
            if (syntaxReference?.GetSyntax() is AttributeSyntax { ArgumentList: { } argumentList })
            {
                foreach (var argument in argumentList.Arguments)
                {
                    if (argument.NameEquals?.Name.Identifier.Text == "VisibleWhen")
                    {
                        return argument.Expression.GetLocation();
                    }
                }
            }

            return member.Symbol.Locations.FirstOrDefault() ?? Location.None;
        }

        private sealed class AnnotatedMember
        {
            public AnnotatedMember(IPropertySymbol symbol, string predicate, string ownServiceId, AttributeData presentation)
            {
                Symbol = symbol;
                Predicate = predicate;
                OwnServiceId = ownServiceId;
                Presentation = presentation;
            }

            public IPropertySymbol Symbol { get; }

            public string Predicate { get; }

            public string OwnServiceId { get; }

            public AttributeData Presentation { get; }
        }
    }
}
