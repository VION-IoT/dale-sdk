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
                                                       start.RegisterSyntaxNodeAction(ctx => AnalyzeAttribute(ctx, allowed),
                                                                                      SyntaxKind.Attribute);
                                                   });
        }

        private static HashSet<string> CollectPropertyGroupConstants(Compilation compilation)
        {
            var allowed = new HashSet<string>();

            foreach (var symbol in compilation.GetSymbolsWithName("PropertyGroup", SymbolFilter.Type))
            {
                if (symbol is not INamedTypeSymbol type || !type.IsStatic)
                {
                    continue;
                }

                foreach (var member in type.GetMembers())
                {
                    if (member is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String } field
                        && field.ConstantValue is string s)
                    {
                        allowed.Add(s);
                    }
                }
            }

            return allowed;
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

                if (arg.Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    // Constant reference (PropertyGroup.Status) — accept.
                    continue;
                }

                var value = literal.Token.ValueText;
                if (allowed.Contains(value))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE026_LiteralGroupKey,
                                                           literal.GetLocation(),
                                                           value));
            }
        }
    }
}
