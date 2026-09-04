using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE026 — Literal string passed as <c>[Presentation(Group = "...")]</c> doesn't match
    ///     any constant declared in a <c>PropertyGroup</c>-named static class in the compilation.
    ///     <para />
    ///     The platform ships <see cref="Vion.Dale.Sdk.Core.PropertyGroup" /> and integrators ship
    ///     their own (e.g. <c>Acme.Vion.Conventions.PropertyGroup</c>). Any <c>PropertyGroup</c>-
    ///     named static class with <c>const string</c> members participates — match by type name,
    ///     not by full name, so integrator vocabularies don't trigger spurious warnings.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LiteralGroupKeyAnalyzer : DiagnosticAnalyzer
    {
        private const string PropertyGroupTypeName = "PropertyGroup";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE026_LiteralGroupKey);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
                                                   {
                                                       var allowed = CollectPropertyGroupConstants(start.Compilation);
                                                       start.RegisterSyntaxNodeAction(ctx => AnalyzeAttribute(ctx, allowed), SyntaxKind.Attribute);
                                                   });
        }

        /// <summary>
        ///     Every string constant of every static class named <c>PropertyGroup</c> the compilation can
        ///     see, in its own assembly and in its references. The references half is what makes this rule
        ///     agree with a consumer's build: the platform's own
        ///     <c>Vion.Dale.Sdk.Core.PropertyGroup</c> is a source declaration only inside the SDK, and
        ///     metadata for everyone else, so a source-only lookup warned every consumer about the keys the
        ///     platform ships. Built once per compilation, and skipped for any assembly whose type-name set
        ///     does not carry the name at all.
        /// </summary>
        private static HashSet<string> CollectPropertyGroupConstants(Compilation compilation)
        {
            var allowed = new HashSet<string>();

            foreach (var assembly in CandidateAssemblies(compilation))
            {
                foreach (var type in PropertyGroupTypes(assembly.GlobalNamespace))
                {
                    foreach (var member in type.GetMembers())
                    {
                        if (member is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String } field && field.ConstantValue is string s)
                        {
                            allowed.Add(s);
                        }
                    }
                }
            }

            return allowed;
        }

        private static IEnumerable<IAssemblySymbol> CandidateAssemblies(Compilation compilation)
        {
            // TypeNames is a flat set the compiler already has, so an assembly that declares no
            // PropertyGroup at all costs one lookup instead of a namespace walk. Every framework and
            // unrelated-package reference falls out here.
            if (compilation.Assembly.TypeNames.Contains(PropertyGroupTypeName))
            {
                yield return compilation.Assembly;
            }

            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referenced && referenced.TypeNames.Contains(PropertyGroupTypeName))
                {
                    yield return referenced;
                }
            }
        }

        /// <summary>
        ///     The static classes named exactly <c>PropertyGroup</c> under <paramref name="container" />,
        ///     in any namespace and nested in a type or not. The name is matched exactly, not as a suffix:
        ///     an integrator declares <c>Acme.PropertyGroup</c>, and a class named <c>EcoPropertyGroup</c>
        ///     is not read. Type members are walked as well as namespace members because the
        ///     <c>GetSymbolsWithName</c> lookup this replaced matched a nested declaration too, and an
        ///     integrator who nests theirs writes the same vocabulary.
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> PropertyGroupTypes(INamespaceOrTypeSymbol container)
        {
            foreach (var type in container.GetTypeMembers())
            {
                if (type.IsStatic && type.Name == PropertyGroupTypeName)
                {
                    yield return type;
                }

                foreach (var nested in PropertyGroupTypes(type))
                {
                    yield return nested;
                }
            }

            if (container is not INamespaceSymbol ns)
            {
                yield break;
            }

            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var type in PropertyGroupTypes(child))
                {
                    yield return type;
                }
            }
        }

        private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context, HashSet<string> allowed)
        {
            var attr = (AttributeSyntax)context.Node;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(attr, context.CancellationToken);

            if (symbolInfo.Symbol is not IMethodSymbol ctor)
            {
                return;
            }

            var attrClass = ctor.ContainingType;
            if (AnalyzerHelper.GetFullName(attrClass) != AnalyzerHelper.PresentationAttribute)
            {
                return;
            }

            if (attr.ArgumentList == null)
            {
                return;
            }

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.Text != "Group")
                {
                    continue;
                }

                if (arg.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    // Constant reference (PropertyGroup.Status) — accept.
                    continue;
                }

                var value = literal.Token.ValueText;
                if (allowed.Contains(value))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE026_LiteralGroupKey, literal.GetLocation(), value));
            }
        }
    }
}