using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public static class DeclarativeInterfaceBinder
    {
        public static void BindInterfacesFromAttributes(object logicBlock, IInterfaceFactory interfaceFactory)
        {
            var type = logicBlock.GetType();

            // Handle class-based interfaces with automatic detection
            BindClassBasedInterfaces(logicBlock, interfaceFactory, type);

            // Handle property-based interfaces with automatic detection
            BindPropertyBasedInterfaces(logicBlock, interfaceFactory, type);
        }

        private static void BindClassBasedInterfaces(object logicBlock, IInterfaceFactory interfaceFactory, Type type)
        {
            // Get all implementation interfaces that the class implements
            var implementedLogicInterfaces = GetImplementedLogicInterfaces(type);

            // Get explicitly defined interface attributes
            var interfaceAttributes = type.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();
            var dependencyAttributes = type.GetCustomAttributes<RequiresLogicBlockInterfaceAttribute>().ToList();

            // Process each implemented interface
            foreach (var implementedLogicInterface in implementedLogicInterfaces)
            {
                BindLogicInterface(logicBlock,
                                   implementedLogicInterface,
                                   interfaceAttributes,
                                   dependencyAttributes,
                                   interfaceFactory,
                                   null);
            }
        }

        private static void BindPropertyBasedInterfaces(object logicBlock, IInterfaceFactory interfaceFactory, Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                var propertyType = property.PropertyType;

                // Check if property type implements any logic interfaces
                var implementedLogicInterfaces = GetImplementedLogicInterfaces(propertyType);

                // Skip if no logic interfaces implemented
                if (implementedLogicInterfaces.Length == 0)
                {
                    continue;
                }

                // Get the value of the property
                var propertyValue = property.GetValue(logicBlock);
                if (propertyValue == null)
                {
                    continue;
                }

                // Get explicitly defined interface attributes for the property
                var interfaceAttributes = property.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();
                var dependencyAttributes = property.GetCustomAttributes<RequiresLogicBlockInterfaceAttribute>().ToList();

                // Process each implemented interface
                foreach (var implementedLogicInterface in implementedLogicInterfaces)
                {
                    // For property-based interfaces, always use PropertyName-InterfaceName pattern
                    // This ensures unique identifiers even with single interface implementation
                    var defaultIdentifier = $"{property.Name}_{implementedLogicInterface.Name}";

                    BindLogicInterface(propertyValue,
                                       implementedLogicInterface,
                                       interfaceAttributes,
                                       dependencyAttributes,
                                       interfaceFactory,
                                       defaultIdentifier);
                }
            }
        }

        private static void BindLogicInterface(object implementation,
                                               Type implementedLogicInterface,
                                               List<LogicBlockInterfaceBindingAttribute> interfaceAttributes,
                                               List<RequiresLogicBlockInterfaceAttribute> dependencyAttributes,
                                               IInterfaceFactory interfaceFactory,
                                               string? defaultIdentifier)
        {
            // Look for explicit attribute for this interface, use explicit attribute or create default
            var interfaceAttribute = interfaceAttributes.FirstOrDefault(attr => attr.ForInterface == implementedLogicInterface) ??
                                     new LogicBlockInterfaceBindingAttribute(implementedLogicInterface);

            var logicSendInterfaceType = FindLogicSendInterface(implementedLogicInterface);
            var identifier = interfaceAttribute.Identifier ?? defaultIdentifier ?? implementedLogicInterface.Name;

            var logicSendInterfaceInstance = CreateLogicSendInterface(interfaceFactory, logicSendInterfaceType, implementedLogicInterface, identifier, implementation);
            var dependencyAttribute = dependencyAttributes.FirstOrDefault(attr => attr.ForInterface == implementedLogicInterface);
            ApplyMetadata(logicSendInterfaceInstance, logicSendInterfaceType, interfaceAttribute, dependencyAttribute);
        }

        private static Type[] GetImplementedLogicInterfaces(Type type)
        {
            return type.GetInterfaces().Where(i => i.GetCustomAttribute<LogicInterfaceAttribute>() != null).ToArray();
        }

        private static Type FindLogicSendInterface(Type implementationType)
        {
            var implementationAttr = implementationType.GetCustomAttribute<LogicInterfaceAttribute>();
            if (implementationAttr?.SenderInterface == null)
            {
                throw new
                    InvalidOperationException($"Implementation interface {implementationType.Name} is missing LogicInterfaceAttribute or the attribute's SendInterface is null.");
            }

            return implementationAttr.SenderInterface;
        }

        private static void ApplyMetadata(object logicSendInterfaceInstance,
                                          Type logicInterfaceType,
                                          LogicBlockInterfaceBindingAttribute? interfaceAttr,
                                          RequiresLogicBlockInterfaceAttribute? dependencyAttr)
        {
            if (logicSendInterfaceInstance is not ILogicSenderInterface logicSendInterface)
            {
                return;
            }

            // Apply basic metadata
            if (!string.IsNullOrEmpty(interfaceAttr?.DefaultName))
            {
                logicSendInterface.WithDefaultName(interfaceAttr.DefaultName);
            }

            if (interfaceAttr?.Tags.Length > 0)
            {
                logicSendInterface.WithTags(interfaceAttr.Tags);
            }

            // Apply dependency metadata if present
            if (dependencyAttr != null)
            {
                ConfigureDependencyDynamic(logicSendInterfaceInstance, logicInterfaceType, dependencyAttr);
            }
        }

        private static void ConfigureDependencyDynamic(object logicSendInterfaceInstance, Type logicInterfaceType, RequiresLogicBlockInterfaceAttribute dependencyAttr)
        {
            // Find the ConfigureDependency extension method
            var configureDependencyMethod = typeof(LogicInterfaceExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                                            .FirstOrDefault(m => m.Name == nameof(LogicInterfaceExtensions.ConfigureDependency) &&
                                                                                                 m.IsGenericMethodDefinition);

            if (configureDependencyMethod == null)
            {
                throw new InvalidOperationException("ConfigureDependency method not found");
            }

            // Make the generic method with the logic interface type (e.g., IToggler)
            var genericMethod = configureDependencyMethod.MakeGenericMethod(logicInterfaceType);

            // Call the method with the dependency attributes
            genericMethod.Invoke(null,
            [
                logicSendInterfaceInstance,
                dependencyAttr.DefaultName,
                dependencyAttr.Cardinality,
                dependencyAttr.Sharing,
                dependencyAttr.CreationType,
                dependencyAttr.Tags,
            ]);
        }

        /// <summary>
        ///     Retrieves all properties that are function interfaces (with or without [Interface] attribute).
        /// </summary>
        private static List<PropertyInfo> GetInterfaceProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true)
                                   .Where(p => (p.GetCustomAttribute<LogicBlockInterfaceBindingAttribute>() != null || IsLogicSendInterfaceType(p.PropertyType)) && p.CanWrite)
                                   .ToList();
        }

        /// <summary>
        ///     Retrieves properties with [Interface] attribute that are invalid (no setter).
        /// </summary>
        private static List<PropertyInfo> GetInvalidInterfaceProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true)
                                   .Where(p => (p.GetCustomAttribute<LogicBlockInterfaceBindingAttribute>() != null || IsLogicSendInterfaceType(p.PropertyType)) && !p.CanWrite)
                                   .ToList();
        }

        /// <summary>
        ///     Determines if a type is a function interface by checking if it derives from ILogicSenderInterface.
        /// </summary>
        private static bool IsLogicSendInterfaceType(Type type)
        {
            return typeof(ILogicSenderInterface).IsAssignableFrom(type);
        }

        private static Type FindImplementationInterface(Type logicInterfaceType)
        {
            // Look for all interfaces in the same containing type (static class)
            var containingType = logicInterfaceType.DeclaringType;
            if (containingType == null)
            {
                throw new InvalidOperationException($"Interface {logicInterfaceType.Name} must be declared within a static class");
            }

            // Find all nested interfaces with LogicFunctionImplementationAttribute pointing to our interface
            var nestedTypes = containingType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static);

            foreach (var nestedType in nestedTypes.Where(t => t.IsInterface))
            {
                var implementationAttr = nestedType.GetCustomAttribute<LogicFunctionImplementationAttribute>();
                if (implementationAttr?.ImplementingFunctionInterface == logicInterfaceType)
                {
                    return nestedType;
                }
            }

            throw new InvalidOperationException($"No implementation interface found for {logicInterfaceType.Name}. " +
                                                $"Ensure there's an interface with [LogicFunctionImplementation(typeof({logicInterfaceType.Name}))] in the same static class.");
        }

        private static object GetImplementationInstance(object logicBlock, Type implementationType, string? implementationProperty)
        {
            if (string.IsNullOrEmpty(implementationProperty))
            {
                // Default: check if the logic block itself implements the interface
                if (implementationType.IsInstanceOfType(logicBlock))
                {
                    return logicBlock;
                }

                throw new InvalidOperationException($"Logic block {logicBlock.GetType().Name} does not implement {implementationType.Name}. " +
                                                    $"Either implement the interface directly or apply [LogicBlockInterfaceBinding] to a property whose value implements it.");
            }

            // Get implementation from specified property
            var property = logicBlock.GetType().GetProperty(implementationProperty, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
            {
                throw new InvalidOperationException($"Implementation property '{implementationProperty}' not found on {logicBlock.GetType().Name}");
            }

            var implementation = property.GetValue(logicBlock);
            if (implementation == null)
            {
                throw new InvalidOperationException($"Implementation property '{implementationProperty}' is null. " +
                                                    $"Ensure it's initialized before calling BindInterfacesFromAttributes.");
            }

            if (!implementationType.IsInstanceOfType(implementation))
            {
                throw new InvalidOperationException($"Object in property '{implementationProperty}' does not implement {implementationType.Name}");
            }

            return implementation;
        }

        private static object CreateLogicSendInterface(IInterfaceFactory interfaceFactory,
                                                       Type logicSendInterfaceType,
                                                       Type logicInterfaceType,
                                                       string identifier,
                                                       object implementation)
        {
            // Use reflection to call the generic Create method
            var createMethod = typeof(IInterfaceFactory).GetMethod(nameof(IInterfaceFactory.Create), BindingFlags.Public | BindingFlags.Instance);
            if (createMethod == null)
            {
                throw new InvalidOperationException("Create method not found on IInterfaceFactory");
            }

            var genericCreateMethod = createMethod.MakeGenericMethod(logicSendInterfaceType, logicInterfaceType);
            return genericCreateMethod.Invoke(interfaceFactory, [identifier, implementation])!;
        }
    }
}