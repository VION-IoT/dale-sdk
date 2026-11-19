using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE049 — an assembly marked [DaleSharedAssembly] that declares handler actors but no IConfigureServices.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SharedHandlerRegistrationAnalyzer : DiagnosticAnalyzer
    {
        private const string DaleSharedAssemblyAttributeName = "Vion.Dale.Sdk.Core.DaleSharedAssemblyAttribute";

        private const string HandlerActorInterfaceName = "Vion.Dale.Sdk.Abstractions.IServiceProviderHandlerActor";

        private const string ConfigureServicesInterfaceName = "Vion.Dale.Sdk.Core.IConfigureServices";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE049_SharedHandlerAssemblyWithoutRegistration);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(AnalyzeCompilation);
        }

        private static void AnalyzeCompilation(CompilationAnalysisContext context)
        {
            var assembly = context.Compilation.Assembly;

            // The plugin loader recognises the mark only as a type another assembly defines, so an
            // assembly declaring its own attribute of that name is not shared and has nothing to answer.
            var mark = assembly.GetAttributes()
                               .FirstOrDefault(a => AnalyzerHelper.GetFullName(a.AttributeClass) == DaleSharedAssemblyAttributeName &&
                                                    !SymbolEqualityComparer.Default.Equals(a.AttributeClass!.ContainingAssembly, assembly));
            if (mark == null)
            {
                return;
            }

            // Concrete means what the runtime's scan means — not abstract, not an interface — at any
            // accessibility, since it reads every type the assembly declares.
            var concreteTypes = AnalyzerHelper.EnumerateDeclaredTypes(assembly).Where(type => !type.IsAbstract && type.TypeKind != TypeKind.Interface).ToList();

            if (concreteTypes.Any(type => Implements(type, ConfigureServicesInterfaceName)))
            {
                return;
            }

            var handlerActors = concreteTypes.Where(type => Implements(type, HandlerActorInterfaceName))
                                             .Select(type => type.ToDisplayString())
                                             .OrderBy(name => name, StringComparer.Ordinal)
                                             .ToList();
            if (handlerActors.Count == 0)
            {
                return;
            }

            var location = mark.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE049_SharedHandlerAssemblyWithoutRegistration,
                                                       location,
                                                       assembly.Name,
                                                       string.Join(", ", handlerActors)));
        }

        private static bool Implements(INamedTypeSymbol type, string interfaceFullName)
        {
            return type.AllInterfaces.Any(i => AnalyzerHelper.GetFullName(i) == interfaceFullName);
        }
    }
}
