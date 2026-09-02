using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public static class DeclarativeServiceBinder
    {
        public static void BindServicesFromAttributes(object logicBlock, ServiceBinder binder, BindingMode mode, IReadOnlyDictionary<string, JsonNode?>? parameterContext)
        {
            var type = logicBlock.GetType();

            // Class-level service: one per logic block, identified by the class name.
            // The dropped [Service] attribute previously allowed overriding the identifier; without
            // it, the class name is canonical. The root service is never gated (the placement matrix says
            // whole-block existence = the operator adds the instance or not).
            var implementedServiceInterfaces = ServiceSurface.GetImplementedServiceInterfaces(type);
            var service = binder.CreateService(type.Name);
            var boundInterfaceProperties = new HashSet<string>();

            foreach (var iface in implementedServiceInterfaces)
            {
                service.Implements(iface, decl => BindInterfaceProperties(logicBlock, iface, decl, boundInterfaceProperties));
            }

            BindExtraProperties(logicBlock, service, boundInterfaceProperties);

            // Scan for properties whose values are themselves services (implement a service interface
            // or carry service-property attributes).
            BindPropertyBasedServices(logicBlock, binder, mode, parameterContext);
        }

        private static void BindPropertyBasedServices(object logicBlock, ServiceBinder binder, BindingMode mode, IReadOnlyDictionary<string, JsonNode?>? parameterContext)
        {
            var type = logicBlock.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                var propertyType = property.PropertyType;

                var implementedServiceInterfaces = ServiceSurface.GetImplementedServiceInterfaces(propertyType);

                // Skip if neither service interfaces nor service-property attributes present. The same
                // predicate decides whether a property-bound interface endpoint has an owning service for
                // its RFC 0019 relation half — hence the shared helper.
                if (!ServiceSurface.IsServiceBearing(propertyType))
                {
                    continue;
                }

                // Skip a gated-out component service entirely in Live mode (its whole service —
                // all properties + measuring points — falls out by construction). Definition mode binds and
                // records the predicate for ServiceInfo.IncludedWhen.
                var includedWhen = InclusionGate.ReadPredicate(property);
                if (!InclusionGate.IsIncluded(includedWhen, mode, parameterContext))
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

                // A declared gate is recorded whatever its text: null means the member carries no
                // [IncludedWhen], and every other value is this member's gate. An empty predicate recorded as
                // "no gate" would put an unconditional member on the wire that Live binding then refuses.
                if (includedWhen is not null)
                {
                    binder.RegisterServiceIncludedWhen(property.Name, includedWhen);
                }
            }
        }

        private static void BindServiceWithInterfaces(object serviceObject, string serviceIdentifier, Type[] implementedServiceInterfaces, ServiceBinder binder)
        {
            var boundInterfaceProperties = new HashSet<string>();
            var service = binder.CreateService(serviceIdentifier);

            // Process all interface implementations using non-generic approach
            foreach (var iface in implementedServiceInterfaces)
            {
                service.Implements(iface, decl => BindInterfaceProperties(serviceObject, iface, decl, boundInterfaceProperties));
            }

            // Then bind extra properties (not mapped to an interface)
            BindExtraProperties(serviceObject, service, boundInterfaceProperties);
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