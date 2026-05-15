using System;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public static class LogicInterfaceExtensions
    {
        public static TInterface WithDefaultName<TInterface>(this TInterface obj, string defaultName)
            where TInterface : ILogicSenderInterface
        {
            obj.GetMetaData().DefaultName = defaultName;
            return obj;
        }

        public static TInterface WithTags<TInterface>(this TInterface obj, params string[] tags)
            where TInterface : ILogicSenderInterface
        {
            obj.GetMetaData().Tags = tags.ToList();
            return obj;
        }

        public static TInterface WithMultiplicity<TInterface>(this TInterface obj, LinkMultiplicity multiplicity)
            where TInterface : ILogicSenderInterface
        {
            obj.GetMetaData().Multiplicity = multiplicity;
            return obj;
        }

        private static LogicSenderInterfaceBase AsImplementation<TInterface>(this TInterface obj)
            where TInterface : ILogicSenderInterface
        {
            if (obj is LogicSenderInterfaceBase logicSenderInterfaceBase)
            {
                return logicSenderInterfaceBase;
            }

            throw new InvalidCastException($"Object of type {typeof(TInterface).FullName} is not a LogicSenderInterfaceBase");
        }

        private static FunctionInterfaceMetaData GetMetaData<TInterface>(this TInterface obj)
            where TInterface : ILogicSenderInterface
        {
            return obj.AsImplementation().MetaData;
        }
    }
}