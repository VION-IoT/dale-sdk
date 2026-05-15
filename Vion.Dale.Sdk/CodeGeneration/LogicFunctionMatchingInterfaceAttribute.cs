using System;

namespace Vion.Dale.Sdk.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class LogicFunctionMatchingInterfaceAttribute : Attribute
    {
        public Type MatchingFunctionInterface { get; }

        public LogicFunctionMatchingInterfaceAttribute(Type matchingFunctionInterface)
        {
            MatchingFunctionInterface = matchingFunctionInterface;
        }
    }
}