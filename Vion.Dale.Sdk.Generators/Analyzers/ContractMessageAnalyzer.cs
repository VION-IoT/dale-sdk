using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Validates [Contract] class structure and nested message attributes:
    ///     DALE009 — BetweenInterface/AndInterface must start with 'I'
    ///     DALE010 — [Command]/[StateUpdate] From/To must match contract interface names
    ///     DALE011 — [RequestResponse] ResponseType must be a struct nested in the same contract class
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ContractMessageAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get =>
                ImmutableArray.Create(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix,
                                      DaleDiagnostics.DALE010_MessageFromToMismatch,
                                      DaleDiagnostics.DALE011_ResponseTypeMustBeNestedStruct);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            var contractAttr = AnalyzerHelper.GetAttribute(type, AnalyzerHelper.LogicBlockContractAttribute);
            if (contractAttr == null)
            {
                return;
            }

            // Extract BetweenInterface and AndInterface from named arguments (required init properties)
            var betweenInterface = AnalyzerHelper.GetNamedArgument<string>(contractAttr, "BetweenInterface");
            var andInterface = AnalyzerHelper.GetNamedArgument<string>(contractAttr, "AndInterface");

            // DALE009: interface names must start with 'I'
            if (betweenInterface != null && !betweenInterface.StartsWith("I"))
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix,
                                                           type.Locations.FirstOrDefault(),
                                                           type.Name,
                                                           "BetweenInterface",
                                                           betweenInterface));
            }

            if (andInterface != null && !andInterface.StartsWith("I"))
            {
                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix,
                                                           type.Locations.FirstOrDefault(),
                                                           type.Name,
                                                           "AndInterface",
                                                           andInterface));
            }

            // Analyze nested message types
            foreach (var nestedType in type.GetTypeMembers())
            {
                AnalyzeMessageType(context, nestedType, type.Name, betweenInterface, andInterface);
            }
        }

        private static void AnalyzeMessageType(SymbolAnalysisContext context, INamedTypeSymbol nestedType, string contractName, string? betweenInterface, string? andInterface)
        {
            // Check [Command] and [StateUpdate] — From/To must match interface names
            var commandAttr = AnalyzerHelper.GetAttribute(nestedType, AnalyzerHelper.CommandAttribute);
            var stateUpdateAttr = AnalyzerHelper.GetAttribute(nestedType, AnalyzerHelper.StateUpdateAttribute);
            var requestResponseAttr = AnalyzerHelper.GetAttribute(nestedType, AnalyzerHelper.RequestResponseAttribute);

            var messageAttr = commandAttr ?? stateUpdateAttr ?? requestResponseAttr;
            if (messageAttr == null)
            {
                return;
            }

            // DALE010: From/To must match BetweenInterface or AndInterface
            var from = AnalyzerHelper.GetNamedArgument<string>(messageAttr, "From");
            var to = AnalyzerHelper.GetNamedArgument<string>(messageAttr, "To");

            if (from != null && betweenInterface != null && andInterface != null)
            {
                if (from != betweenInterface && from != andInterface)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE010_MessageFromToMismatch,
                                                               nestedType.Locations.FirstOrDefault(),
                                                               nestedType.Name,
                                                               "From",
                                                               from,
                                                               betweenInterface,
                                                               andInterface));
                }
            }

            if (to != null && betweenInterface != null && andInterface != null)
            {
                if (to != betweenInterface && to != andInterface)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE010_MessageFromToMismatch,
                                                               nestedType.Locations.FirstOrDefault(),
                                                               nestedType.Name,
                                                               "To",
                                                               to,
                                                               betweenInterface,
                                                               andInterface));
                }
            }

            // DALE011: RequestResponse ResponseType must be a struct nested in the same contract class
            if (requestResponseAttr != null)
            {
                var responseType = AnalyzerHelper.GetNamedArgument<INamedTypeSymbol>(requestResponseAttr, "ResponseType");
                if (responseType == null)
                {
                    // ResponseType might be in ConstructorArguments as a typeof expression
                    foreach (var arg in requestResponseAttr.NamedArguments)
                    {
                        if (arg.Key == "ResponseType" && arg.Value.Value is INamedTypeSymbol namedType)
                        {
                            responseType = namedType;
                            break;
                        }

                        if (arg.Key == "ResponseType" && arg.Value.Kind == TypedConstantKind.Type)
                        {
                            responseType = arg.Value.Value as INamedTypeSymbol;
                            break;
                        }
                    }
                }

                if (responseType != null)
                {
                    var isStruct = responseType.IsValueType && responseType.TypeKind == TypeKind.Struct;
                    var isNestedInSameContract = SymbolEqualityComparer.Default.Equals(responseType.ContainingType, nestedType.ContainingType);

                    if (!isStruct || !isNestedInSameContract)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE011_ResponseTypeMustBeNestedStruct,
                                                                   nestedType.Locations.FirstOrDefault(),
                                                                   nestedType.Name,
                                                                   responseType.Name,
                                                                   contractName));
                    }
                }
            }
        }
    }
}
