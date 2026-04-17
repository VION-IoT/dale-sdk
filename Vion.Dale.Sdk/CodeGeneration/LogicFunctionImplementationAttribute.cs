using System;

namespace Vion.Dale.Sdk.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class LogicFunctionImplementationAttribute : Attribute
    {
        public Type ImplementingFunctionInterface { get; }

        public LogicFunctionImplementationAttribute(Type implementingFunctionInterface)
        {
            ImplementingFunctionInterface = implementingFunctionInterface;
        }
    }
}