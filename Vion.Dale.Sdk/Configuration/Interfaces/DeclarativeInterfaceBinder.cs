using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public static class DeclarativeInterfaceBinder
    {
        public static void BindInterfacesFromAttributes(object logicBlock,
                                                        IInterfaceFactory interfaceFactory,
                                                        ServiceBinder serviceBinder,
                                                        BindingMode mode,
                                                        IReadOnlyDictionary<string, JsonNode?>? parameterContext,
                                                        Dictionary<string, string> mintedBy)
        {
            var type = logicBlock.GetType();

            // Both paths below mint into the block's one namespace: a class-level binding and a
            // property-level one can pin the same Identifier, and only the second would have survived into
            // the endpoint dictionary the introspection reads.

            // Handle class-based interfaces with automatic detection. Class-implemented interfaces are not
            // gateable (no member to carry [IncludedWhen] — DALE043 enforces this), so they bind unconditionally.
            BindClassBasedInterfaces(logicBlock, interfaceFactory, serviceBinder, type, mintedBy);

            // Handle property-based interfaces with automatic detection (the gateable path).
            BindPropertyBasedInterfaces(logicBlock,
                                        interfaceFactory,
                                        serviceBinder,
                                        type,
                                        mode,
                                        parameterContext,
                                        mintedBy);
        }

        private static void BindClassBasedInterfaces(object logicBlock,
                                                     IInterfaceFactory interfaceFactory,
                                                     ServiceBinder serviceBinder,
                                                     Type type,
                                                     Dictionary<string, string> mintedBy)
        {
            // Get all implementation interfaces that the class implements
            var implementedLogicInterfaces = GetImplementedLogicInterfaces(type);

            // Get explicitly defined interface attributes
            var interfaceAttributes = type.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();

            // Process each implemented interface
            foreach (var implementedLogicInterface in implementedLogicInterfaces)
            {
                BindLogicInterface(logicBlock,
                                   implementedLogicInterface,
                                   interfaceAttributes,
                                   interfaceFactory,
                                   null,
                                   null,
                                   serviceBinder,

                                   // A class-implemented endpoint belongs to the root service, which the service
                                   // binder creates unconditionally from the class name.
                                   type.Name,

                                   // A class-implemented binding has no member to name, so it is named by the
                                   // interface it binds — which is what distinguishes it from its peers in a
                                   // refusal, where naming the class would only repeat the block the message
                                   // already names.
                                   implementedLogicInterface.Name,
                                   type,
                                   mintedBy);
            }
        }

        private static void BindPropertyBasedInterfaces(object logicBlock,
                                                        IInterfaceFactory interfaceFactory,
                                                        ServiceBinder serviceBinder,
                                                        Type type,
                                                        BindingMode mode,
                                                        IReadOnlyDictionary<string, JsonNode?>? parameterContext,
                                                        Dictionary<string, string> mintedBy)
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

                // Skip a gated-out interface binding entirely in Live mode (never bound, never
                // wired, never published). Definition mode always binds and records the predicate.
                var includedWhen = InclusionGate.ReadPredicate(property);
                if (includedWhen is not null && mode == BindingMode.Definition)
                {
                    InclusionGate.EnsureResolvable(includedWhen, logicBlock, property.Name);
                }

                if (!InclusionGate.IsIncluded(includedWhen, mode, parameterContext))
                {
                    continue;
                }

                // A null component cannot serve a message, so Live binding skips it. The definition view
                // describes the TYPE: an endpoint's identity is the property name and the interface, both
                // known without an instance — and a client that cannot see the endpoint cannot see the gate
                // that removes it either. Nothing dispatches in Definition mode, so there is nothing behind it
                // to need.
                var propertyValue = property.GetValue(logicBlock);
                if (propertyValue == null && mode == BindingMode.Live)
                {
                    continue;
                }

                // Get explicitly defined interface attributes for the property
                var interfaceAttributes = property.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();

                // The component service that owns this property's endpoints — the same service the
                // service binder mints from the property name. A component without a service surface has no
                // node in the cloud graph, so its endpoints own no relation half (null); DALE045 warns.
                var owningServiceIdentifier = ServiceSurface.IsServiceBearing(propertyType) ? property.Name : null;

                // Process each implemented interface
                foreach (var implementedLogicInterface in implementedLogicInterfaces)
                {
                    // For property-based interfaces, always use PropertyName-InterfaceName pattern
                    // This ensures unique identifiers even with single interface implementation
                    var defaultIdentifier = $"{property.Name}_{implementedLogicInterface.Name}";

                    BindLogicInterface(propertyValue,
                                       implementedLogicInterface,
                                       interfaceAttributes,
                                       interfaceFactory,
                                       defaultIdentifier,
                                       includedWhen,
                                       serviceBinder,
                                       owningServiceIdentifier,
                                       property.Name,
                                       type,
                                       mintedBy);
                }
            }
        }

        private static void BindLogicInterface(object? implementation,
                                               Type implementedLogicInterface,
                                               List<LogicBlockInterfaceBindingAttribute> interfaceAttributes,
                                               IInterfaceFactory interfaceFactory,
                                               string? defaultIdentifier,
                                               string? includedWhen,
                                               ServiceBinder serviceBinder,
                                               string? owningServiceIdentifier,
                                               string memberName,
                                               Type logicBlockType,
                                               Dictionary<string, string> mintedBy)
        {
            // Look for explicit attribute for this interface, use explicit attribute or create default
            var interfaceAttribute = interfaceAttributes.FirstOrDefault(attr => attr.ForInterface == implementedLogicInterface) ??
                                     new LogicBlockInterfaceBindingAttribute(implementedLogicInterface);

            var logicSendInterfaceType = FindLogicSendInterface(implementedLogicInterface);
            var identifier = interfaceAttribute.Identifier ?? defaultIdentifier ?? implementedLogicInterface.Name;
            BindingIdentifiers.Claim(mintedBy, identifier, memberName, "Interface binding", logicBlockType);

            // A null implementation reaches here only from the definition view, which describes a type rather
            // than serving messages (BindPropertyBasedInterfaces skips a null component in Live mode). The
            // factory would hand it to the generated RegisterInstance, whose ConditionalWeakTable refuses a
            // null key — so the endpoint is described directly instead, with the same identifier, metadata
            // and relation halves and no dispatch registration behind it.
            var logicSendInterfaceInstance = implementation is null ? DescribeLogicSendInterface(interfaceFactory, logicSendInterfaceType, implementedLogicInterface, identifier) :
                                                 CreateLogicSendInterface(interfaceFactory, logicSendInterfaceType, implementedLogicInterface, identifier, implementation);
            ApplyMetadata(logicSendInterfaceInstance, interfaceAttribute, includedWhen);

            RegisterServiceRelations(implementedLogicInterface, identifier, serviceBinder, owningServiceIdentifier);
        }

        /// <summary>
        ///     Derives this endpoint's service-relation halves from the <c>[ServiceRelation]</c>
        ///     declarations on the contract its logic interface belongs to — one half per declaration, on the
        ///     service that owns the endpoint.
        ///     <para>
        ///         The load-bearing invariant: the half is registered <b>here</b>, by the same code path that
        ///         just minted <paramref name="identifier" />, so a relation's <c>interfaceIdentifier</c> can
        ///         never diverge from the endpoint's actual wiring identifier (class-level override, bare
        ///         interface name, or <c>{Property}_{Interface}</c>). There is no second resolution rule.
        ///     </para>
        ///     <para>
        ///         Registration is keyed by service identifier, so it is order-independent with respect to the
        ///         service binder that creates those services afterwards — the introspection joins by key.
        ///     </para>
        /// </summary>
        private static void RegisterServiceRelations(Type implementedLogicInterface, string identifier, ServiceBinder serviceBinder, string? owningServiceIdentifier)
        {
            var contractType = implementedLogicInterface.GetCustomAttribute<LogicInterfaceAttribute>()?.ContractType;
            if (contractType == null)
            {
                return;
            }

            var relationAttributes = contractType.GetCustomAttributes<ServiceRelationAttribute>().ToList();
            if (relationAttributes.Count == 0)
            {
                return;
            }

            var contractAttribute = contractType.GetCustomAttribute<LogicBlockContractAttribute>();
            if (contractAttribute == null)
            {
                throw new InvalidOperationException($"[ServiceRelation] on '{contractType.FullName}' requires the class to also carry [LogicBlockContract] — " +
                                                    "the relation's two sides are the contract's BetweenInterface / AndInterface.");
            }

            foreach (var relationAttribute in relationAttributes)
            {
                // Validate before the owning-service check so a mis-declared contract fails loudly even on a
                // block whose endpoint would not have emitted a half anyway.
                var namesBetweenSide = relationAttribute.OutwardsInterface == contractAttribute.BetweenInterface;
                if (!namesBetweenSide && relationAttribute.OutwardsInterface != contractAttribute.AndInterface)
                {
                    throw new
                        InvalidOperationException($"[ServiceRelation(RelationType = \"{relationAttribute.RelationType}\")] on '{contractType.FullName}' declares OutwardsInterface = " +
                                                  $"\"{relationAttribute.OutwardsInterface}\", which is neither the contract's BetweenInterface (\"{contractAttribute.BetweenInterface}\") " +
                                                  $"nor its AndInterface (\"{contractAttribute.AndInterface}\").");
                }

                // No owning service → no node in the cloud graph to anchor the edge to. The
                // endpoint still binds and wires; the omission is flagged at compile time by DALE045.
                if (owningServiceIdentifier == null)
                {
                    continue;
                }

                serviceBinder.RegisterServiceRelation(owningServiceIdentifier,
                                                      new ServiceRelationInfo
                                                      {
                                                          RelationType = relationAttribute.RelationType,
                                                          InterfaceIdentifier = identifier,
                                                          InterfaceTypeFullName = ReflectionHelper.GetDisplayFullName(implementedLogicInterface),
                                                          Direction = implementedLogicInterface.Name == relationAttribute.OutwardsInterface ?
                                                                          ServiceRelationDirection.Outwards : ServiceRelationDirection.Inwards,
                                                      });
            }
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

        private static void ApplyMetadata(object logicSendInterfaceInstance, LogicBlockInterfaceBindingAttribute interfaceAttr, string? includedWhen)
        {
            if (logicSendInterfaceInstance is not ILogicSenderInterface logicSendInterface)
            {
                return;
            }

            if (!string.IsNullOrEmpty(interfaceAttr.DefaultName))
            {
                logicSendInterface.WithDefaultName(interfaceAttr.DefaultName);
            }

            if (interfaceAttr.Tags.Length > 0)
            {
                logicSendInterface.WithTags(interfaceAttr.Tags);
            }

            logicSendInterface.WithMultiplicity(interfaceAttr.Multiplicity);
            logicSendInterface.WithIncludedWhen(includedWhen);
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

        // The definition-view path: build the sender instance and register it under its identifier without the
        // dispatch wiring an implementation would carry. Mirrors what the factory does, minus the extension
        // registration a null implementation cannot satisfy.
        private static object DescribeLogicSendInterface(IInterfaceFactory interfaceFactory, Type logicSendInterfaceType, Type logicInterfaceType, string identifier)
        {
            var createMethod = typeof(IInterfaceFactory).GetMethod(nameof(IInterfaceFactory.Describe), BindingFlags.Public | BindingFlags.Instance);
            if (createMethod == null)
            {
                throw new InvalidOperationException("Describe method not found on IInterfaceFactory");
            }

            return createMethod.MakeGenericMethod(logicSendInterfaceType, logicInterfaceType).Invoke(interfaceFactory, [identifier])!;
        }

        private static object CreateLogicSendInterface(IInterfaceFactory interfaceFactory,
                                                       Type logicSendInterfaceType,
                                                       Type logicInterfaceType,
                                                       string identifier,
                                                       object? implementation)
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