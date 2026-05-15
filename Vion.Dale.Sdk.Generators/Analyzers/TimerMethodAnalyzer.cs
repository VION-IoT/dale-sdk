using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Validates [Timer] attribute usage:
    ///     DALE002 — method must be void and parameterless
    ///     DALE005 — interval must be greater than zero
    ///     DALE012 — duplicate timer identifiers within the same class
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TimerMethodAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get =>
                ImmutableArray.Create(DaleDiagnostics.DALE002_TimerMethodSignature,
                                      DaleDiagnostics.DALE005_TimerIntervalMustBePositive,
                                      DaleDiagnostics.DALE012_DuplicateTimerIdentifier);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Use SymbolAction for DALE002 and DALE005 (per-method checks)
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);

            // Use SymbolAction on NamedType for DALE012 (cross-method duplicate check)
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            var timerAttr = AnalyzerHelper.GetAttribute(method, AnalyzerHelper.TimerAttribute);
            if (timerAttr == null)
            {
                return;
            }

            // DALE002: method must be void and parameterless
            if (!method.ReturnsVoid || method.Parameters.Length > 0)
            {
                var issues = new List<string>();
                if (!method.ReturnsVoid)
                {
                    issues.Add("returns " + method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                }

                if (method.Parameters.Length > 0)
                {
                    issues.Add("has " + method.Parameters.Length + " parameter(s)");
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE002_TimerMethodSignature,
                                                           method.Locations.FirstOrDefault(),
                                                           method.Name,
                                                           string.Join(" and ", issues)));
            }

            // DALE005: interval must be > 0
            if (timerAttr.ConstructorArguments.Length > 0)
            {
                var intervalArg = timerAttr.ConstructorArguments[0];
                if (intervalArg.Value is double intervalValue && intervalValue <= 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE005_TimerIntervalMustBePositive, method.Locations.FirstOrDefault(), method.Name, intervalValue));
                }
            }
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            // Collect all [Timer] methods with their effective identifiers
            var timerMethods = new List<(string Identifier, IMethodSymbol Method)>();

            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            {
                var timerAttr = AnalyzerHelper.GetAttribute(member, AnalyzerHelper.TimerAttribute);
                if (timerAttr == null)
                {
                    continue;
                }

                // Effective identifier: explicit Identifier argument, or method name
                string? explicitId = null;
                if (timerAttr.ConstructorArguments.Length > 1)
                {
                    explicitId = timerAttr.ConstructorArguments[1].Value as string;
                }

                var effectiveId = explicitId ?? member.Name;
                timerMethods.Add((effectiveId, member));
            }

            // DALE012: check for duplicate identifiers
            var seen = new Dictionary<string, IMethodSymbol>();
            foreach (var (identifier, method) in timerMethods)
            {
                if (seen.TryGetValue(identifier, out var existingMethod))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE012_DuplicateTimerIdentifier,
                                                               method.Locations.FirstOrDefault(),
                                                               existingMethod.Name,
                                                               method.Name,
                                                               identifier));
                }
                else
                {
                    seen[identifier] = method;
                }
            }
        }
    }
}
