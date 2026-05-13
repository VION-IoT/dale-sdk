using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Scriban;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Generators
{
    [Generator]
    public class LogicClassGenerator : IIncrementalGenerator
    {
        private const string SenderInterfaceSuffix = "SenderInterface";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Create a pipeline that finds all classes with LogicBlockContractAttribute
            var contractClasses = context.SyntaxProvider
                                         .CreateSyntaxProvider(static (s, _) => IsCandidateClass(s), static (ctx, _) => GetClassDeclaration(ctx))
                                         .Where(static m => m is not null);

            // Combine with compilation
            var compilationAndClasses = context.CompilationProvider.Combine(contractClasses.Collect());

            // Register the source output
            context.RegisterSourceOutput(compilationAndClasses, (spc, source) => Execute(spc, source.Left, source.Right!));
        }

        private static bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0 &&
                   classDeclaration.AttributeLists.SelectMany(al => al.Attributes).Any(a => a.Name.ToString().Contains("Contract"));
        }

        private static ClassDeclarationSyntax? GetClassDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            // Verify the class actually has the LogicBlockContractAttribute
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            if (symbol is null)
            {
                return null;
            }

            var hasLogicBlockContractAttribute = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(LogicBlockContractAttribute));

            return hasLogicBlockContractAttribute ? classDeclaration : null;
        }

        private void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> contractClasses)
        {
            context.LogInfo($"{nameof(LogicClassGenerator)} running...");

            if (contractClasses.IsDefaultOrEmpty)
            {
                return;
            }

            ProcessContracts(context, compilation, contractClasses.ToList());
        }

        private void ProcessContracts(SourceProductionContext context, Compilation compilation, List<ClassDeclarationSyntax> contractClasses)
        {
            var contractAttributeType = GetAttributeType<LogicBlockContractAttribute>(compilation);
            if (contractAttributeType == null)
            {
                context.LogInfo("LogicBlockContractAttribute not found, skipping message-centric contract processing");
                return;
            }

            context.LogInfo($"Processing {contractClasses.Count} message-centric contracts");

            foreach (var contractClass in contractClasses)
            {
                var classSymbol = GetClassSymbol(compilation, contractClass);
                if (classSymbol == null)
                {
                    context.LogInfo($"Class symbol not found for  {contractClass.Identifier}, skipping");
                    continue;
                }

                var contractAttribute = GetAttribute(classSymbol, contractAttributeType);
                if (contractAttribute == null)
                {
                    context.LogInfo($"LogicBlockContractAttribute found for  {classSymbol.Name}, skipping");
                    continue;
                }

                var contractInfo = ExtractContractInfo(context, contractAttribute, classSymbol.Name);
                if (contractInfo == null)
                {
                    context.LogInfo($"LogicBlockContractAttribute info could not be extracted for  {classSymbol.Name}, skipping");
                    continue;
                }

                context.LogInfo($"Processing contract: {contractInfo.Identifier}");

                var messageAnalysis = AnalyzeContractMessages(context, compilation, classSymbol, contractInfo);
                GenerateContractFiles(context, contractInfo, messageAnalysis, classSymbol.ContainingNamespace.ToDisplayString(), classSymbol.ToDisplayString());
            }
        }

        private static INamedTypeSymbol? GetClassSymbol(Compilation compilation, ClassDeclarationSyntax contractClass)
        {
            return compilation.GetSemanticModel(contractClass.SyntaxTree).GetDeclaredSymbol(contractClass);
        }

        private static INamedTypeSymbol? GetAttributeType<TAttribute>(Compilation compilation)
        {
            return compilation.GetTypeByMetadataName(typeof(TAttribute).FullName!);
        }

        private static AttributeData? GetAttribute(INamedTypeSymbol classType, INamedTypeSymbol attributeType)
        {
            return GetAttributes(classType, attributeType).FirstOrDefault();
        }

        private static List<AttributeData> GetAttributes(INamedTypeSymbol classType, INamedTypeSymbol attributeType)
        {
            return classType.GetAttributes().Where(a => AreSymbolsEqual(a.AttributeClass, attributeType)).ToList();
        }

        private static bool AreSymbolsEqual(INamedTypeSymbol? attrClass, INamedTypeSymbol? requestResponseAttr)
        {
            return SymbolEqualityComparer.Default.Equals(attrClass, requestResponseAttr);
        }

        private static ContractInfo? ExtractContractInfo(SourceProductionContext context, AttributeData contractAttribute, string className)
        {
            string? betweenInterface = null;
            string? andInterface = null;

            foreach (var namedArg in contractAttribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case nameof(LogicBlockContractAttribute.BetweenInterface):
                        betweenInterface = namedArg.Value.Value as string;
                        break;
                    case nameof(LogicBlockContractAttribute.AndInterface):
                        andInterface = namedArg.Value.Value as string;
                        break;
                }
            }

            if (string.IsNullOrEmpty(betweenInterface) || string.IsNullOrEmpty(andInterface))
            {
                context.LogInfo("LogicBlockContractAttribute is missing required properties, skipping");
                return null;
            }

            if (!betweenInterface!.StartsWith("I") || !andInterface!.StartsWith("I"))
            {
                context.LogInfo("LogicBlockContractAttribute interfaces must start with 'I', skipping");
                return null;
            }

            return new ContractInfo
                   {
                       Identifier = className,
                       BetweenInterface = betweenInterface,
                       AndInterface = andInterface,
                   };
        }

        private static MessageAnalysis AnalyzeContractMessages(SourceProductionContext context, Compilation compilation, INamedTypeSymbol contractClass, ContractInfo contractInfo)
        {
            var analysis = new MessageAnalysis
                           {
                               BetweenInterfaceSendCapabilities = [],
                               BetweenInterfaceHandleCapabilities = [],
                               AndInterfaceSendCapabilities = [],
                               AndInterfaceHandleCapabilities = [],
                           };

            var commandAttr = GetAttributeType<CommandAttribute>(compilation);
            var stateUpdateAttr = GetAttributeType<StateUpdateAttribute>(compilation);
            var requestResponseAttr = GetAttributeType<RequestResponseAttribute>(compilation);

            var contractTypeMembers = contractClass.GetTypeMembers().ToList();

            foreach (var messageType in contractTypeMembers)
            {
                if (messageType.TypeKind != TypeKind.Struct)
                {
                    context.LogInfo($"Skipping non-struct message type: {messageType.Name}");
                    continue;
                }

                foreach (var attribute in messageType.GetAttributes())
                {
                    var attrClass = attribute.AttributeClass;
                    var messageTypeName = $"{contractInfo.Identifier}.{messageType.Name}";

                    if (AreSymbolsEqual(attrClass, commandAttr)) // Command
                    {
                        ProcessCommandAttribute(context, attribute, messageTypeName, contractInfo, analysis);
                    }
                    else if (AreSymbolsEqual(attrClass, stateUpdateAttr)) // StateUpdate
                    {
                        ProcessStateUpdateAttribute(context, attribute, messageTypeName, contractInfo, analysis);
                    }
                    else if (AreSymbolsEqual(attrClass, requestResponseAttr)) // RequestResponse
                    {
                        ProcessRequestResponseAttribute(context, attribute, messageTypeName, contractInfo, analysis);
                    }
                    else
                    {
                        context.LogInfo($"Unknown attribute on message type: {messageType.Name}, skipping");
                    }
                }
            }

            return analysis;
        }

        private static void ProcessCommandAttribute(SourceProductionContext context,
                                                    AttributeData attribute,
                                                    string messageTypeName,
                                                    ContractInfo contractInfo,
                                                    MessageAnalysis analysis)
        {
            var (valid, from, to) = ExtractFromTo(context, contractInfo, attribute, messageTypeName);
            if (!valid)
            {
                context.LogInfo($"Invalid {nameof(CommandAttribute)} attribute on message type: {messageTypeName}, skipping");
                return;
            }

            AddSendCapability(analysis, from, contractInfo, "ISendCommand", messageTypeName);
            AddHandleCapability(analysis, to, contractInfo, "IHandleCommand", messageTypeName);
        }

        private static void ProcessStateUpdateAttribute(SourceProductionContext context,
                                                        AttributeData attribute,
                                                        string messageTypeName,
                                                        ContractInfo contractInfo,
                                                        MessageAnalysis analysis)
        {
            var (valid, from, to) = ExtractFromTo(context, contractInfo, attribute, messageTypeName);
            if (!valid)
            {
                context.LogInfo($"Invalid {nameof(StateUpdateAttribute)} attribute on message type: {messageTypeName}, skipping");
                return;
            }

            AddSendCapability(analysis, from, contractInfo, "ISendStateUpdate", messageTypeName);
            AddHandleCapability(analysis, to, contractInfo, "IHandleStateUpdate", messageTypeName);
        }

        private static void ProcessRequestResponseAttribute(SourceProductionContext context,
                                                            AttributeData attribute,
                                                            string messageTypeName,
                                                            ContractInfo contractInfo,
                                                            MessageAnalysis analysis)
        {
            var (valid, from, to, responseType) = ExtractFromToResponse(context, contractInfo, attribute, messageTypeName);
            if (!valid)
            {
                context.LogInfo($"Invalid {nameof(RequestResponseAttribute)} on message type: {messageTypeName}, skipping");
                return;
            }

            var responseTypeName = $"{contractInfo.Identifier}.{responseType.Name}";

            // Add capabilities based on from/to
            AddSendCapability(analysis, from, contractInfo, "ISendRequest", messageTypeName);
            AddHandleCapability(analysis, from, contractInfo, "IHandleResponse", responseTypeName);
            AddHandleCapability(analysis,
                                to,
                                contractInfo,
                                "IHandleRequest",
                                messageTypeName,
                                responseTypeName);
        }

        private static void AddSendCapability(MessageAnalysis analysis, string interfaceName, ContractInfo contractInfo, string interfaceType, string messageType)
        {
            var capability = new SendCapability { InterfaceType = interfaceType, MessageType = messageType };

            if (interfaceName == contractInfo.BetweenInterface)
            {
                analysis.BetweenInterfaceSendCapabilities.Add(capability);
            }
            else if (interfaceName == contractInfo.AndInterface)
            {
                analysis.AndInterfaceSendCapabilities.Add(capability);
            }
        }

        private static void AddHandleCapability(MessageAnalysis analysis,
                                                string interfaceName,
                                                ContractInfo contractInfo,
                                                string interfaceType,
                                                string messageType,
                                                string? responseType = null)
        {
            var capability = new HandleCapability
                             {
                                 InterfaceType = interfaceType,
                                 MessageType = messageType,
                                 ResponseType = responseType,
                             };

            if (interfaceName == contractInfo.BetweenInterface)
            {
                analysis.BetweenInterfaceHandleCapabilities.Add(capability);
            }
            else if (interfaceName == contractInfo.AndInterface)
            {
                analysis.AndInterfaceHandleCapabilities.Add(capability);
            }
        }

        private static (bool valid, string from, string to) ExtractFromTo(SourceProductionContext context,
                                                                          ContractInfo contractInfo,
                                                                          AttributeData attribute,
                                                                          string messageTypeName)
        {
            string? from = null;
            string? to = null;

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case nameof(IFromToAttribute.From):
                        from = namedArg.Value.Value as string;
                        break;
                    case nameof(IFromToAttribute.To):
                        to = namedArg.Value.Value as string;
                        break;
                }
            }

            if (string.IsNullOrEmpty(from))
            {
                context.LogInfo($"Invalid attribute value {nameof(IFromToAttribute.From)} on message type: {messageTypeName}, skipping");
                return (false, "", "");
            }

            if (string.IsNullOrEmpty(to))
            {
                context.LogInfo($"Invalid attribute value {nameof(IFromToAttribute.To)} on message type: {messageTypeName}, skipping");
                return (false, "", "");
            }

            if (from != contractInfo.BetweenInterface && from != contractInfo.AndInterface)
            {
                context.LogInfo($"Attribute value {nameof(IFromToAttribute.From)} does not match any contract interface on message type: {messageTypeName}, skipping");
                return (false, "", "");
            }

            if (to != contractInfo.BetweenInterface && to != contractInfo.AndInterface)
            {
                context.LogInfo($"Attribute value {nameof(IFromToAttribute.To)} does not match any contract interface on message type: {messageTypeName}, skipping");
                return (false, "", "");
            }

            return (true, from, to);
        }

        private static (bool valid, string from, string to, INamedTypeSymbol responseType) ExtractFromToResponse(SourceProductionContext context,
                                                                                                                 ContractInfo contractInfo,
                                                                                                                 AttributeData attribute,
                                                                                                                 string messageTypeName)
        {
            string? from = null;
            string? to = null;
            INamedTypeSymbol? responseType = null;

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case nameof(RequestResponseAttribute.From):
                        from = namedArg.Value.Value as string;
                        break;
                    case nameof(RequestResponseAttribute.To):
                        to = namedArg.Value.Value as string;
                        break;
                    case nameof(RequestResponseAttribute.ResponseType):
                        responseType = namedArg.Value.Value as INamedTypeSymbol;
                        break;
                }
            }

            if (string.IsNullOrEmpty(from))
            {
                context.LogInfo($"Invalid attribute value {nameof(RequestResponseAttribute.From)} on message type: {messageTypeName}, skipping");
                return (false, "", "", null!);
            }

            if (string.IsNullOrEmpty(to))
            {
                context.LogInfo($"Invalid attribute value {nameof(RequestResponseAttribute.To)} on message type: {messageTypeName}, skipping");
                return (false, "", "", null!);
            }

            if (from != contractInfo.BetweenInterface && from != contractInfo.AndInterface)
            {
                context.LogInfo($"Attribute value {nameof(RequestResponseAttribute.From)} does not match any contract interface on message type: {messageTypeName}, skipping");
                return (false, "", "", null!);
            }

            if (to != contractInfo.BetweenInterface && to != contractInfo.AndInterface)
            {
                context.LogInfo($"Attribute value {nameof(RequestResponseAttribute.To)} does not match any contract interface on message type: {messageTypeName}, skipping");
                return (false, "", "", null!);
            }

            if (responseType == null)
            {
                context.LogInfo($"Invalid attribute value {nameof(RequestResponseAttribute.ResponseType)} on message type: {messageTypeName}, skipping");
                return (false, "", "", null!);
            }

            return (true, from, to, responseType);
        }

        private void GenerateContractFiles(SourceProductionContext context, ContractInfo contractInfo, MessageAnalysis analysis, string namespaceName, string contractTypeName)
        {
            // Generate for first interface (BetweenInterface)
            GenerateContractFile(context,
                                 namespaceName,
                                 contractInfo.Identifier,
                                 contractTypeName,
                                 contractInfo.BetweenInterface,
                                 contractInfo.AndInterface,
                                 analysis.BetweenInterfaceSendCapabilities,
                                 analysis.BetweenInterfaceHandleCapabilities);

            // Generate for second interface (AndInterface)
            GenerateContractFile(context,
                                 namespaceName,
                                 contractInfo.Identifier,
                                 contractTypeName,
                                 contractInfo.AndInterface,
                                 contractInfo.BetweenInterface,
                                 analysis.AndInterfaceSendCapabilities,
                                 analysis.AndInterfaceHandleCapabilities);
        }

        private void GenerateContractFile(SourceProductionContext context,
                                          string namespaceName,
                                          string contractIdentifier,
                                          string contractTypeName,
                                          string interfaceName,
                                          string matchingInterface,
                                          List<SendCapability> sendCapabilities,
                                          List<HandleCapability> handleCapabilities)
        {
            var sendInterfaceName = interfaceName + SenderInterfaceSuffix;

            // Create the model for this interface pair with both interface definitions and implementation class
            var model = CreateLogicClassModelFromCapabilities(namespaceName,
                                                              interfaceName,
                                                              matchingInterface,
                                                              sendInterfaceName,
                                                              matchingInterface + SenderInterfaceSuffix,
                                                              contractTypeName,
                                                              sendCapabilities,
                                                              handleCapabilities);

            // Generate the complete file using existing template logic
            var templateText = LoadScribanTemplate(context);
            var template = ParseScribanTemplate(context, templateText);

            var source = template.Render(model.ToScribanModel(), member => member.Name);
            var fileName = $"{contractIdentifier}.{interfaceName}.g.cs";

            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            context.LogInfo($"Generated complete file: {fileName}");
        }

        private LogicClassModel CreateLogicClassModelFromCapabilities(string namespaceName,
                                                                      string interfaceName,
                                                                      string matchingInterfaceName,
                                                                      string sendInterfaceName,
                                                                      string matchingSendInterfaceName,
                                                                      string contractTypeName,
                                                                      List<SendCapability> sendCapabilities,
                                                                      List<HandleCapability> handleCapabilities)
        {
            // Initialize lists for send interface data
            var sendBaseInterfaces = new List<string> { "ILogicSenderInterface" };
            var methods = new List<LogicClassModel.MethodData>();

            // Populate send interface data
            foreach (var capability in sendCapabilities)
            {
                sendBaseInterfaces.Add($"{capability.InterfaceType}<{capability.MessageType}>");

                // Add method to the implementation class using existing logic
                methods.Add(new LogicClassModel.MethodData
                            {
                                Name = GetMethodNameFromInterface(capability.InterfaceType),
                                Parameters = GetParametersFromCapability(capability),
                                Body = GetMethodBodyFromInterface(capability.InterfaceType),
                            });
            }

            // Initialize lists for implementation interface data
            var handleBaseInterfaces = new List<string> { "ILogicHandlerInterface" };
            var messageCases = new List<LogicClassModel.MessageCaseData>();

            foreach (var capability in handleCapabilities)
            {
                if (!string.IsNullOrEmpty(capability.ResponseType))
                {
                    handleBaseInterfaces.Add($"{capability.InterfaceType}<{capability.MessageType}, {capability.ResponseType}>");
                }
                else
                {
                    handleBaseInterfaces.Add($"{capability.InterfaceType}<{capability.MessageType}>");
                }

                // Add message case to the implementation class using existing logic
                messageCases.Add(new LogicClassModel.MessageCaseData
                                 {
                                     MessageType = capability.MessageType,
                                     Body = GetHandleBodyFromInterface(capability.InterfaceType,
                                                                       !string.IsNullOrEmpty(capability.ResponseType)),
                                 });
            }

            // Generate extension methods using existing logic
            var extensionMethods = GenerateExtensionMethodsFromCapabilities(sendCapabilities,
                                                                            interfaceName,
                                                                            sendInterfaceName,
                                                                            matchingSendInterfaceName.Replace(SenderInterfaceSuffix, "")
                                                                                                     .Substring(1)); // Remove "I" from matching interface

            // Create and return the model with all required properties
            var model = new LogicClassModel
                        {
                            NamespaceName = namespaceName,
                            HandlerInterface = new LogicClassModel.HandlerInterfaceData
                                               {
                                                   Name = interfaceName,
                                                   MatchingInterfaceName = matchingInterfaceName,
                                                   SendInterfaceName = sendInterfaceName,
                                                   ContractTypeName = contractTypeName,
                                                   BaseInterfaces = handleBaseInterfaces,
                                               },
                            SenderInterface = new LogicClassModel.SenderInterfaceData
                                              {
                                                  Name = sendInterfaceName,
                                                  ClassName = sendInterfaceName.Substring(1),
                                                  BaseInterfaces = sendBaseInterfaces,
                                                  Methods = methods,
                                                  MessageCases = messageCases,
                                              },
                            ExtensionMethods = extensionMethods,
                        };

            return model;
        }

        private static string GetMethodNameFromInterface(string interfaceType)
        {
            return interfaceType switch
            {
                "ISendCommand" => "SendCommand",
                "ISendRequest" => "SendRequest",
                "ISendStateUpdate" => "SendStateUpdate",
                _ => "UnknownMethod",
            };
        }

        private static string GetParametersFromCapability(SendCapability capability)
        {
            return capability.InterfaceType switch
            {
                "ISendCommand" => $"InterfaceId functionId, {capability.MessageType} command",
                "ISendRequest" => $"InterfaceId functionId, {capability.MessageType} request",
                "ISendStateUpdate" => $"{capability.MessageType} update",
                _ => $"{capability.MessageType} data",
            };
        }

        private static string GetMethodBodyFromInterface(string interfaceType)
        {
            return interfaceType switch
            {
                "ISendCommand" => "SendToFunction(functionId, command);",
                "ISendRequest" => "SendToFunction(functionId, request);",
                "ISendStateUpdate" => "SendToAllLinkedFunctions(update);",
                _ => "// Unknown interface type",
            };
        }

        private static string GetHandleBodyFromInterface(string interfaceType, bool hasResponse)
        {
            return interfaceType switch
            {
                "IHandleCommand" => "_implementation.HandleCommand(m.Data);",
                "IHandleRequest" when hasResponse => "SendToFunction(m.FromId, _implementation.HandleRequest(m.Data));",
                "IHandleRequest" => "_implementation.HandleRequest(m.Data);",
                "IHandleResponse" => "_implementation.HandleResponse(m.FromId, m.Data);",
                "IHandleStateUpdate" => "_implementation.HandleStateUpdate(m.FromId, m.Data);",
                _ => "// Unknown interface type",
            };
        }

        private static List<LogicClassModel.ExtensionMethodData> GenerateExtensionMethodsFromCapabilities(List<SendCapability> sendCapabilities,
                                                                                                          string implementationType,
                                                                                                          string logicType,
                                                                                                          string linkedClassName)
        {
            var extensionMethods = new List<LogicClassModel.ExtensionMethodData>();

            foreach (var capability in sendCapabilities)
            {
                var methodName = GetMethodNameFromInterface(capability.InterfaceType);
                var parameters = GetParametersFromCapability(capability);
                var parameterNames = ExtractParameterNames(parameters);

                extensionMethods.Add(new LogicClassModel.ExtensionMethodData
                                     {
                                         Name = methodName,
                                         Parameters = parameters,
                                         ParameterNames = parameterNames,
                                         ImplementationType = implementationType,
                                         LogicType = logicType,
                                         LinkedClassName = linkedClassName,
                                     });
            }

            return extensionMethods;
        }

        private static string ExtractParameterNames(string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
            {
                return "";
            }

            return string.Join(", ", parameters.Split(',').Select(param => param.Trim()).Select(param => param.Split(' ').Last()).Where(name => !string.IsNullOrEmpty(name)));
        }

        private static string LoadScribanTemplate(SourceProductionContext context)
        {
            var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            var resourceName = resourceNames.SingleOrDefault(n => n.EndsWith("LogicClassTemplate.scriban"));
            if (resourceName == null)
            {
                context.LogError("LogicClassTemplate.scriban not found in embedded resources.");
                foreach (var name in resourceNames)
                {
                    context.LogError(name);
                }
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var templateText = reader.ReadToEnd();
            return templateText;
        }

        private static Template ParseScribanTemplate(SourceProductionContext context, string templateText)
        {
            var template = Template.Parse(templateText);
            if (template.HasErrors)
            {
                context.LogError("Template errors:");
                foreach (var msg in template.Messages)
                {
                    context.LogError(msg.ToString());
                }
            }

            return template;
        }

        // Helper classes for message analysis
        private class ContractInfo
        {
            public required string Identifier { get; init; }

            public required string BetweenInterface { get; init; }

            public required string AndInterface { get; init; }
        }

        private class MessageAnalysis
        {
            public required List<SendCapability> BetweenInterfaceSendCapabilities { get; init; }

            public required List<HandleCapability> BetweenInterfaceHandleCapabilities { get; init; }

            public required List<SendCapability> AndInterfaceSendCapabilities { get; init; }

            public required List<HandleCapability> AndInterfaceHandleCapabilities { get; init; }
        }

        private class SendCapability
        {
            public required string InterfaceType { get; init; }

            public required string MessageType { get; init; }
        }

        private class HandleCapability
        {
            public required string InterfaceType { get; init; }

            public required string MessageType { get; init; }

            public string? ResponseType { get; init; }
        }
    }
}
