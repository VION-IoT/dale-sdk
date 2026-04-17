using System;

namespace Vion.Dale.Sdk.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class LogicInterfaceAttribute : Attribute
    {
        public required Type MatchingInterface { get; init; }

        public required Type SenderInterface { get; init; }

        public required Type ContractType { get; init; }
    }
}