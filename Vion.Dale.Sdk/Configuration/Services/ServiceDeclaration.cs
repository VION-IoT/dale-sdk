using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Core;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServiceDeclaration<TServiceInterface> : ServiceDeclarationBase
    {
        public ServiceDeclaration(ServiceBinder binder, string serviceIdentifier) : base(binder, serviceIdentifier, typeof(TServiceInterface))
        {
        }

        /// <summary>
        ///     Binds a property of the source object to a property of the service interface.
        /// </summary>
        public ServiceDeclaration<TServiceInterface> BindProperty<TSource, TProp>(Expression<Func<TServiceInterface, TProp>> serviceInterfaceProperty,
                                                                                  TSource source,
                                                                                  Expression<Func<TSource, TProp>> sourceGetter,
                                                                                  Action<TSource, TProp>? sourceSetter = null)
            where TSource : class
        {
            var servicePropertyName = ReflectionHelper.GetSinglePropertyName(serviceInterfaceProperty);
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterPropertyBinding(servicePropertyName,
                                    source,
                                    fullPath,
                                    rootPropertyInfo,
                                    targetPropertyType,
                                    s => compiledGetter((TSource)s),
                                    sourceSetter != null ? (s, v) => sourceSetter((TSource)s, (TProp)v!) : null);

            return this;
        }

        /// <summary>
        ///     Binds a measuring point of the source object to a property of the service interface.
        /// </summary>
        public ServiceDeclaration<TServiceInterface> BindMeasuringPoint<TSource, TProp>(Expression<Func<TServiceInterface, TProp>> serviceInterfaceProperty,
                                                                                        TSource source,
                                                                                        Expression<Func<TSource, TProp>> sourceGetter,
                                                                                        Action<TSource, TProp>? sourceSetter = null)
            where TSource : class
        {
            var serviceMeasuringPointName = ReflectionHelper.GetSinglePropertyName(serviceInterfaceProperty);
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterMeasuringPointBinding(serviceMeasuringPointName,
                                          source,
                                          fullPath,
                                          rootPropertyInfo,
                                          targetPropertyType,
                                          s => compiledGetter((TSource)s),
                                          sourceSetter != null ? (s, v) => sourceSetter((TSource)s, (TProp)v!) : null);

            return this;
        }

        /// <summary>
        ///     Defines a relationship between this service and a function interface instance.
        ///     The relation type and direction are determined from the ServiceRelationAttribute on TServiceInterface.
        /// </summary>
        public ServiceDeclaration<TServiceInterface> DefineRelation<TInterface>(TInterface logicSendInterfaceInstance)
            where TInterface : ILogicSenderInterface
        {
            var serviceInterfaceType = typeof(TServiceInterface);
            var functionInterfaceType = logicSendInterfaceInstance.LogicInterfaceType;

            // Get the relation attribute from TServiceInterface
            var serviceRelationAttribute =
                serviceInterfaceType.GetCustomAttributes<ServiceRelationAttribute>().FirstOrDefault(a => a.FunctionInterfaceType == functionInterfaceType);
            if (serviceRelationAttribute == null)
            {
                throw new InvalidOperationException($"Service interface {serviceInterfaceType.Name} does not have a ServiceRelationAttribute declaration.");
            }

            // Find the interface identifier from the instance
            var interfaceIdentifier = FindInterfaceIdentifier(logicSendInterfaceInstance);

            // Create relation info
            var relationInfo = new ServiceRelationInfo
                               {
                                   RelationType = serviceRelationAttribute.RelationType,
                                   InterfaceIdentifier = interfaceIdentifier,
                                   InterfaceTypeFullName = ReflectionHelper.GetDisplayFullName(functionInterfaceType),
                                   Direction = serviceRelationAttribute.Direction,
                                   Annotations = serviceRelationAttribute.Annotations,
                               };

            RegisterServiceRelation(relationInfo);

            return this;
        }

        private static string FindInterfaceIdentifier<TInterface>(TInterface interfaceInstance)
            where TInterface : ILogicSenderInterface
        {
            // For now, return the instance's identifier property if available
            // In a more complete implementation, you'd have a registry of interface instances to identifiers
            return interfaceInstance.GetType().GetProperty(nameof(LogicSenderInterfaceBase.Identifier))?.GetValue(interfaceInstance)?.ToString() ??
                   throw new InvalidOperationException("Could not determine interface identifier from instance.");
        }
    }
}