using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public static class DeclarativeContractBinder
    {
        public static void BindContractsFromAttributes(object logicBlock, IContractFactory contractFactory)
        {
            var type = logicBlock.GetType();
            var contractProperties = GetContractProperties(type);
            var invalidContractProperties = GetInvalidContractProperties(type);

            foreach (var property in invalidContractProperties)
            {
                throw new InvalidOperationException($"Property '{property.Name}' in '{type.Name}' has [ServiceProviderContract] attribute but no setter. " +
                                                    $"Contract properties must have at least a private setter to enable binding. " +
                                                    $"Example: public {property.PropertyType.Name} {property.Name} {{ get; private set; }}");
            }

            foreach (var property in contractProperties)
            {
                var contractAttribute = property.GetCustomAttribute<ServiceProviderContractAttribute>();
                var identifier = contractAttribute?.Identifier ?? property.Name;
                var contractInstance = contractFactory.Create(property.PropertyType, identifier);
                property.SetValue(logicBlock, contractInstance);
                ApplyMetadata(contractInstance, contractAttribute);
            }
        }

        private static List<PropertyInfo> GetContractProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true).Where(p => IsContractType(p.PropertyType) && p.CanWrite).ToList();
        }

        private static List<PropertyInfo> GetInvalidContractProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true).Where(p => IsContractType(p.PropertyType) && !p.CanWrite).ToList();
        }

        private static void ApplyMetadata(object contractInstance, ServiceProviderContractAttribute? contractAttr)
        {
            if (contractAttr == null)
            {
                return;
            }

            if (contractInstance is not LogicBlockContractBase logicBlockContract)
            {
                throw new InvalidCastException($"Object of type {contractInstance.GetType().FullName} is not of type {nameof(LogicBlockContractBase)}");
            }

            var metadata = logicBlockContract.MetaData;
            if (!string.IsNullOrEmpty(contractAttr.DefaultName))
            {
                metadata.DefaultName = contractAttr.DefaultName;
            }

            if (contractAttr.Tags.Length > 0)
            {
                metadata.Tags = contractAttr.Tags.ToList();
            }

            metadata.Cardinality = contractAttr.Cardinality;
            metadata.Sharing = contractAttr.Sharing;
        }

        private static bool IsContractType(Type type)
        {
            return type.GetCustomAttribute<ServiceProviderContractTypeAttribute>() != null;
        }
    }
}