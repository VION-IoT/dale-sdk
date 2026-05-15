using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public static class DeclarativeServiceBinder
    {
        public static void BindServicesFromAttributes(object logicBlock, ServiceBinder binder)
        {
            var type = logicBlock.GetType();

            // Class-level service: one per logic block, identified by the class name.
            // The dropped [Service] attribute previously allowed overriding the identifier; without
            // it, the class name is canonical.
            var implementedServiceInterfaces = GetImplementedServiceInterfaces(type);
            var service = binder.CreateService(type.Name);
            var boundInterfaceProperties = new HashSet<string>();

            foreach (var iface in implementedServiceInterfaces)
            {
                service.Implements(iface,
                                   decl =>
                                   {
                                       BindInterfaceProperties(logicBlock, iface, decl, boundInterfaceProperties);
                                       AutoDetectServiceRelationsForInterface(logicBlock, iface, decl);
                                   });
            }

            BindExtraProperties(logicBlock, service, boundInterfaceProperties);

            // Scan for properties whose values are themselves services (implement a service interface
            // or carry service-property attributes).
            BindPropertyBasedServices(logicBlock, binder);
        }

        private static void BindPropertyBasedServices(object logicBlock, ServiceBinder binder)
        {
            var type = logicBlock.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                var propertyType = property.PropertyType;

                var implementedServiceInterfaces = GetImplementedServiceInterfaces(propertyType);
                var hasServiceProperties = HasServicePropertiesOrMeasuringPoints(propertyType);

                // Skip if neither service interfaces nor service-property attributes present.
                if (implementedServiceInterfaces.Length == 0 && !hasServiceProperties)
                {
                    continue;
                }

                var propertyValue = property.GetValue(logicBlock);
                if (propertyValue == null)
                {
                    continue;
                }

                // Use the property name as the service identifier.
                BindServiceWithInterfaces(propertyValue, property.Name, implementedServiceInterfaces, binder);
            }
        }

        private static bool HasServicePropertiesOrMeasuringPoints(Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<ServicePropertyAttribute>() != null || prop.GetCustomAttribute<ServiceMeasuringPointAttribute>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BindServiceWithInterfaces(object serviceObject, string serviceIdentifier, Type[] implementedServiceInterfaces, ServiceBinder binder)
        {
            var boundInterfaceProperties = new HashSet<string>();
            var service = binder.CreateService(serviceIdentifier);

            // Process all interface implementations using non-generic approach
            foreach (var iface in implementedServiceInterfaces)
            {
                service.Implements(iface,
                                   decl =>
                                   {
                                       BindInterfaceProperties(serviceObject, iface, decl, boundInterfaceProperties);
                                       AutoDetectServiceRelationsForInterface(serviceObject, iface, decl);
                                   });
            }

            // Then bind extra properties (not mapped to an interface)
            BindExtraProperties(serviceObject, service, boundInterfaceProperties);
        }

        private static Type[] GetImplementedServiceInterfaces(Type type)
        {
            return type.GetInterfaces().Where(i => i.GetCustomAttribute<ServiceInterfaceAttribute>() != null).ToArray();
        }

        private static void BindInterfaceProperties(object logicBlock, Type interfaceType, ServiceDeclarationBase serviceDecl, HashSet<string> boundInterfaceProperties)
        {
            var interfaceProps = interfaceType.GetProperties().ToDictionary(p => p.Name, p => p);
            var logicBlockProps = logicBlock.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, p => p);

            // Bind all interface properties by name (convention over configuration)
            foreach (var interfaceProp in interfaceProps.Values)
            {
                // Check if logic block has a matching property by name
                if (logicBlockProps.TryGetValue(interfaceProp.Name, out var logicBlockProp))
                {
                    // Verify type compatibility
                    if (!AreTypesCompatible(logicBlockProp.PropertyType, interfaceProp.PropertyType))
                    {
                        continue; // Skip incompatible types
                    }

                    if (interfaceProp.GetCustomAttribute<ServicePropertyAttribute>() != null)
                    {
                        serviceDecl.BindPropertyWithCompiledExpression(interfaceProp.Name, logicBlock, logicBlockProp);
                        boundInterfaceProperties.Add(interfaceProp.Name);
                    }

                    if (interfaceProp.GetCustomAttribute<ServiceMeasuringPointAttribute>() != null)
                    {
                        serviceDecl.BindMeasuringPointWithCompiledExpression(interfaceProp.Name, logicBlock, logicBlockProp);
                        boundInterfaceProperties.Add(interfaceProp.Name);
                    }
                }
            }
        }

        private static void BindExtraProperties(object logicBlock, ServiceBuilderBase service, HashSet<string> boundInterfaceProperties)
        {
            // Process properties with the ServicePropertyAttribute that are NOT already bound to interfaces
            var extrasServiceProperties = ReflectionHelper.GetPropertiesWithAttribute<ServicePropertyAttribute>(logicBlock.GetType(), false)
                                                          .Where(p => !boundInterfaceProperties.Contains(p.Name))
                                                          .ToList();

            foreach (var prop in extrasServiceProperties)
            {
                service.BindPropertyWithCompiledExpression(prop.Name, logicBlock, prop);
            }

            // Process properties with the ServiceMeasuringPointAttribute that are NOT already bound to interfaces
            var extraMeasuringPoints = ReflectionHelper.GetPropertiesWithAttribute<ServiceMeasuringPointAttribute>(logicBlock.GetType(), false)
                                                       .Where(p => !boundInterfaceProperties.Contains(p.Name))
                                                       .ToList();

            foreach (var prop in extraMeasuringPoints)
            {
                service.BindMeasuringPointWithCompiledExpression(prop.Name, logicBlock, prop);
            }
        }

        private static void AutoDetectServiceRelationsForInterface(object logicBlock, Type serviceInterfaceType, ServiceDeclarationBase serviceDecl)
        {
            var logicBlockType = logicBlock.GetType();

            // Get all ServiceRelationAttributes for this specific service interface
            var serviceRelationAttributes = serviceInterfaceType.GetCustomAttributes<ServiceRelationAttribute>().ToList();

            // Get all implemented logic interfaces on the logic block
            var implementedLogicInterfaces = logicBlockType.GetInterfaces().Where(i => i.GetCustomAttribute<LogicInterfaceAttribute>() != null).ToList();
            var interfaceAttributes = logicBlockType.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();

            foreach (var serviceRelationAttribute in serviceRelationAttributes)
            {
                // find the corresponding logic interface implemented by the logic block
                var matchingImplementedLogicInterfaces = implementedLogicInterfaces.Where(li => li.IsAssignableFrom(serviceRelationAttribute.FunctionInterfaceType)).ToList();

                if (matchingImplementedLogicInterfaces.Count == 1)
                {
                    var implementedLogicInterface = matchingImplementedLogicInterfaces.Single();

                    // Look for explicit attribute for this interface, use explicit attribute or create default
                    var interfaceAttribute = interfaceAttributes.FirstOrDefault(attr => attr.ForInterface == implementedLogicInterface);
                    var interfaceIdentifier = interfaceAttribute?.Identifier ?? implementedLogicInterface.Name;

                    // Create auto-detected relation info
                    var relationInfo = new ServiceRelationInfo
                                       {
                                           RelationType = serviceRelationAttribute.RelationType,
                                           InterfaceIdentifier = interfaceIdentifier,
                                           InterfaceTypeFullName = ReflectionHelper.GetDisplayFullName(serviceRelationAttribute.FunctionInterfaceType),
                                           Direction = serviceRelationAttribute.Direction,
                                           Annotations = serviceRelationAttribute.Annotations,
                                       };

                    // Register the auto-detected relation directly via ServiceDeclarationBase
                    serviceDecl.RegisterServiceRelation(relationInfo);
                }
                else if (matchingImplementedLogicInterfaces.Count > 1)
                {
                    // Multiple matches - cannot auto-detect currently, additional conventions would be needed
                }
            }
        }

        private static bool AreTypesCompatible(Type logicBlockType, Type interfaceType)
        {
            // Exact match
            if (logicBlockType == interfaceType)
            {
                return true;
            }

            // Assignment compatibility - logic block type can be assigned to interface type
            if (interfaceType.IsAssignableFrom(logicBlockType))
            {
                return true;
            }

            // Handle nullable types
            var logicBlockUnderlyingType = Nullable.GetUnderlyingType(logicBlockType);
            var interfaceUnderlyingType = Nullable.GetUnderlyingType(interfaceType);

            if (logicBlockUnderlyingType != null && interfaceUnderlyingType != null)
            {
                return AreTypesCompatible(logicBlockUnderlyingType, interfaceUnderlyingType);
            }

            if (logicBlockUnderlyingType != null)
            {
                return AreTypesCompatible(logicBlockUnderlyingType, interfaceType);
            }

            if (interfaceUnderlyingType != null)
            {
                return AreTypesCompatible(logicBlockType, interfaceUnderlyingType);
            }

            return false;
        }
    }
}