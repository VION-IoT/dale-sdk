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
        private static readonly string[] MessageAttributeNames =
        {
            AnalyzerHelper.CommandAttribute,
            AnalyzerHelper.StateUpdateAttribute,
            AnalyzerHelper.RequestResponseAttribute,
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get =>
                ImmutableArray.Create(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix,
                                      DaleDiagnostics.DALE010_MessageFromToMismatch,
                                      DaleDiagnostics.DALE011_ResponseTypeMustBeNestedStruct,
                                      DaleDiagnostics.DALE047_MessageStructNotNestedInContract);
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
                ReportIfStrayMessageStruct(context, type);
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

        /// <summary>
        ///     DALE047 — a message struct the generator will never reach. The three message attributes allow
        ///     any struct target, and <see cref="AnalyzeType" /> only ever walks the structs nested inside a
        ///     <c>[LogicBlockContract]</c> class, so a struct declared beside the contract — or inside any
        ///     other type — compiles and produces nothing.
        /// </summary>
        private static void ReportIfStrayMessageStruct(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Struct)
            {
                return;
            }

            foreach (var attributeName in MessageAttributeNames)
            {
                if (AnalyzerHelper.GetAttribute(type, attributeName) is null)
                {
                    continue;
                }

                // The container decides: nested inside a contract class is the one legal home.
                if (type.ContainingType is { } container && AnalyzerHelper.GetAttribute(container, AnalyzerHelper.LogicBlockContractAttribute) is not null)
                {
                    return;
                }

                context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE047_MessageStructNotNestedInContract,
                                                           type.Locations.FirstOrDefault(),
                                                           type.Name,
                                                           attributeName.Substring(attributeName.LastIndexOf('.') + 1).Replace("Attribute", "")));
                return;
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