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
        /// <summary>
        ///     The longest interval a timer can be scheduled at, mirroring
        ///     <c>DeclarativeTimerBinder.MaxIntervalSeconds</c> — which is the source of truth, and which
        ///     throws at configuration for anything above it.
        /// </summary>
        private const double MaxIntervalSeconds = 4294967;

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

                // The whole refusal set the timer binder applies at configuration
                // (DeclarativeTimerBinder.ResolveInterval), stated as one positive condition so that NaN —
                // which is false against every comparison, so `<= 0` let it through — is refused with the
                // rest. Infinity and a value longer than a clock can wait are the same door.
                if (intervalArg.Value is double intervalValue && !(intervalValue > 0 && intervalValue <= MaxIntervalSeconds))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE005_TimerIntervalMustBePositive, method.Locations.FirstOrDefault(), method.Name, intervalValue));
                }
            }
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            // Every [Timer] this type carries, base declarations included, base-first — the binder
            // collects the whole chain (DeclarativeTimerBinder.GetDeclaredTimerMethods), so a base and a
            // derived method sharing an identifier both reach the callback map and one silently never
            // ticks. Reading only this type's own members missed exactly that.
            var timerMethods = CollectTimerMethods(type);

            // DALE012: check for duplicate identifiers
            var seen = new Dictionary<string, IMethodSymbol>();
            foreach (var (identifier, method) in timerMethods)
            {
                if (!seen.TryGetValue(identifier, out var existingMethod))
                {
                    seen[identifier] = method;
                    continue;
                }

                // Report only where the colliding declaration is this type's own. Without this a
                // three-deep hierarchy would report a base pair again at every type below it.
                if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, type))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE012_DuplicateTimerIdentifier,
                                                           method.Locations.FirstOrDefault(),
                                                           existingMethod.Name,
                                                           method.Name,
                                                           identifier));
            }
        }

        /// <summary>
        ///     Every <c>[Timer]</c> method of <paramref name="type" /> and its bases with its effective
        ///     identifier, base declarations first. An <c>override</c> and the virtual it overrides are one
        ///     timer to the binder and appear once; a <c>new</c> declaration is a second method and appears
        ///     twice, which is the collision.
        /// </summary>
        private static List<(string Identifier, IMethodSymbol Method)> CollectTimerMethods(INamedTypeSymbol type)
        {
            var chain = new List<INamedTypeSymbol>();
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                chain.Add(current);
            }

            chain.Reverse();

            var timerMethods = new List<(string Identifier, IMethodSymbol Method)>();
            var definitions = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var declaring in chain)
            {
                foreach (var member in declaring.GetMembers().OfType<IMethodSymbol>())
                {
                    var timerAttr = AnalyzerHelper.GetAttribute(member, AnalyzerHelper.TimerAttribute);
                    if (timerAttr == null || !definitions.Add(RootDefinition(member)))
                    {
                        continue;
                    }

                    // Effective identifier: explicit Identifier argument, or method name
                    string? explicitId = null;
                    if (timerAttr.ConstructorArguments.Length > 1)
                    {
                        explicitId = timerAttr.ConstructorArguments[1].Value as string;
                    }

                    timerMethods.Add((explicitId ?? member.Name, member));
                }
            }

            return timerMethods;
        }

        private static IMethodSymbol RootDefinition(IMethodSymbol method)
        {
            var current = method;
            while (current.OverriddenMethod is not null)
            {
                current = current.OverriddenMethod;
            }

            return current.OriginalDefinition;
        }
    }
}