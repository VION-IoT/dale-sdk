using System.Collections.Generic;
using System.Linq;

namespace Vion.Dale.Sdk.Generators
{
    public class LogicClassModel
    {
        /// <summary>
        ///     Represents the namespace in which the class is defined, e.g.
        ///     "MyCompany.MyNamespace".
        /// </summary>
        public required string NamespaceName { get; init; }

        /// <summary>
        ///     Extension methods to be generated for the contract interface for simplifying calls to the send interface.
        /// </summary>
        public required List<ExtensionMethodData> ExtensionMethods { get; init; }

        /// <summary>
        ///     Data for the code generator to generate the contract interface
        /// </summary>
        public required HandlerInterfaceData HandlerInterface { get; init; }

        /// <summary>
        ///     Data for the code generator to generate the send interface
        /// </summary>
        public required SenderInterfaceData SenderInterface { get; init; }

        public object ToScribanModel()
        {
            return new
                   {
                       namespace_name = NamespaceName,
                       handler_interface = new
                                           {
                                               name = HandlerInterface.Name,
                                               matching_interface_name = HandlerInterface.MatchingInterfaceName,
                                               send_interface_name = HandlerInterface.SendInterfaceName,
                                               contract_type_name = HandlerInterface.ContractTypeName,
                                               base_interfaces = HandlerInterface.BaseInterfaces,
                                           },
                       sender_interface = new
                                          {
                                              name = SenderInterface.Name,
                                              class_name = SenderInterface.ClassName,
                                              base_interfaces = SenderInterface.BaseInterfaces,
                                              methods = SenderInterface.Methods
                                                                       .Select(m => new
                                                                                    {
                                                                                        name = m.Name,
                                                                                        parameters = m.Parameters,
                                                                                        body = m.Body,
                                                                                    })
                                                                       .ToList(),
                                              message_cases = SenderInterface.MessageCases
                                                                             .Select(m => new
                                                                                          {
                                                                                              message_type = m.MessageType,
                                                                                              body = m.Body,
                                                                                          })
                                                                             .ToList(),
                                          },
                       extension_methods = ExtensionMethods.Select(e => new
                                                                        {
                                                                            name = e.Name,
                                                                            parameters = e.Parameters,
                                                                            parameter_names = e.ParameterNames,
                                                                            implementation_type = e.ImplementationType,
                                                                            logic_type = e.LogicType,
                                                                            linked_class_name = e.LinkedClassName,
                                                                        })
                                                           .ToList(),
                   };
        }

        public class MethodData
        {
            public required string Name { get; init; }

            public required string Parameters { get; init; }

            public required string Body { get; init; }
        }

        public class MessageCaseData
        {
            public required string MessageType { get; init; }

            public required string Body { get; init; }
        }

        public class ExtensionMethodData
        {
            public required string Name { get; init; }

            public required string Parameters { get; init; }

            public required string ParameterNames { get; init; }

            public required string ImplementationType { get; init; }

            public required string LogicType { get; init; }

            public required string LinkedClassName { get; init; }
        }

        public class SenderInterfaceData
        {
            public required string Name { get; init; }

            public required string ClassName { get; init; }

            public required List<string> BaseInterfaces { get; init; } = [];

            public required List<MethodData> Methods { get; init; }

            public required List<MessageCaseData> MessageCases { get; init; }
        }

        public class HandlerInterfaceData
        {
            public required string Name { get; init; }

            public required string MatchingInterfaceName { get; init; }

            public required string SendInterfaceName { get; init; }

            public required string ContractTypeName { get; init; }

            public required List<string> BaseInterfaces { get; init; } = [];
        }
    }
}