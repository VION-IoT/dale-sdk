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

            // Before either walk: a binding attribute neither walk can reach is authored intent nothing
            // reads. Both walks key on the [LogicInterface] marker and the property walk sees public
            // properties only, so an attribute outside those two conditions is silently dropped along with
            // its Identifier, DefaultName, Tags and Multiplicity.
            RefuseUnreadableInterfaceBindings(type);

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

        /// <summary>
        ///     Refuses every <c>[LogicBlockInterfaceBinding]</c> the two walks below cannot reach, naming the
        ///     member and what is wrong with it.
        ///     <para>
        ///         Three shapes reach nothing: a class-level binding for an interface the class does not
        ///         implement, a property-level binding for an interface the property's type does not
        ///         implement, and a binding on a non-public property — the property walk is public-only and
        ///         the block's endpoints are its published wiring surface, so widening it would mint
        ///         endpoints rather than refuse a mistake. In all three the attribute is dropped whole, the
        ///         pinned identifier with it, and a topology authored against that identifier stops matching a
        ///         block that reports itself healthy.
        ///     </para>
        /// </summary>
        private static void RefuseUnreadableInterfaceBindings(Type type)
        {
            foreach (var attribute in type.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>())
            {
                if (!GetImplementedLogicInterfaces(type).Contains(attribute.ForInterface))
                {
                    throw new
                        InvalidOperationException($"Logic block '{type.FullName}' carries [LogicBlockInterfaceBinding(typeof({attribute.ForInterface.Name}))] but does not implement " +
                                                  $"'{attribute.ForInterface.Name}', so the binding — its Identifier included — is read by nothing. Implement the interface, " +
                                                  "move the attribute to the property whose type does, or name the interface the class implements.");
                }
            }

            var boundProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in ReflectionHelper.GetProperties(type, true))
            {
                var attributes = property.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>().ToList();
                if (attributes.Count == 0)
                {
                    continue;
                }

                if (!boundProperties.Contains(property))
                {
                    throw new
                        InvalidOperationException($"Property '{property.Name}' in '{type.FullName}' carries [LogicBlockInterfaceBinding] but is not public, and only public properties " +
                                                  "bind interface endpoints. Make the property public, or remove the attribute.");
                }

                var implemented = GetImplementedLogicInterfaces(property.PropertyType);

                foreach (var attribute in attributes.Where(candidate => !implemented.Contains(candidate.ForInterface)))
                {
                    throw new
                        InvalidOperationException($"Property '{property.Name}' in '{type.FullName}' carries [LogicBlockInterfaceBinding(typeof({attribute.ForInterface.Name}))] but its type " +
                                                  $"'{property.PropertyType.Name}' does not implement '{attribute.ForInterface.Name}', so the binding — its Identifier included — is " +
                                                  "read by nothing. Name an interface the property's type implements, or remove the attribute.");
                }
            }
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
            var logicSendInterfaceInstance = Unwrapping(memberName,
                                                        logicBlockType,
                                                        identifier,
                                                        () => implementation is null ?
                                                                  DescribeLogicSendInterface(interfaceFactory, logicSendInterfaceType, implementedLogicInterface, identifier) :
                                                                  CreateLogicSendInterface(interfaceFactory,
                                                                                           logicSendInterfaceType,
                                                                                           implementedLogicInterface,
                                                                                           identifier,
                                                                                           implementation));
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

        /// <summary>
        ///     Runs an endpoint's construction and turns what the factory refuses into a refusal a consumer
        ///     can act on.
        ///     <para>
        ///         Both factory entries are reached by <see cref="MethodBase.Invoke(object, object[])" />, so
        ///         everything they throw arrives wrapped in a <see cref="TargetInvocationException" /> whose own
        ///         message is "Exception has been thrown by the target of an invocation." — the text the block
        ///         then records as its configuration failure and repeats on every later refusal. The factory's
        ///         own messages name the sender interface at best and never the endpoint, which is the only
        ///         thing an author can look up; this is the one site where the member, the block and the
        ///         identifier are all in scope.
        ///     </para>
        /// </summary>
        private static object Unwrapping(string memberName, Type logicBlockType, string identifier, Func<object> build)
        {
            try
            {
                return build();
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException($"Interface binding '{identifier}' on '{memberName}' in logic block '{logicBlockType.FullName}' could not be built: " +
                                                    exception.InnerException.Message,
                                                    exception.InnerException);
            }
        }
    }
}