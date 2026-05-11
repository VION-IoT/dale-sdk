using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Contracts.Introspection;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Introspection
{
    public static class LogicBlockIntrospection
    {
        public static LogicBlockIntrospectionResult IntrospectLogicBlock(LogicBlockBase logicBlock, IServiceProvider serviceProvider)
        {
            Dictionary<string, LogicBlockContractBase> contracts = new();
            Dictionary<string, LogicSenderInterfaceBase> interfaces = new();
            var serviceBinder = new ServiceBinder();

            var logicBlockSetup = CreateLogicBlockConfigurationBuilder(contracts, interfaces, serviceBinder, serviceProvider);
            InvokeConfigureMethod(logicBlock, logicBlockSetup);

            var logicBlockAnnotations = GetLogicBlockAnnotations(logicBlock.GetType());

            return new LogicBlockIntrospectionResult
                   {
                       TypeFullName = logicBlock.GetType().FullName!,
                       Interfaces = GetInterfaces(interfaces),
                       Contracts = GetContracts(contracts),
                       Services = GetServices(serviceBinder),
                       Annotations = logicBlockAnnotations,
                   };
        }

        private static LogicBlockConfigurationBuilder CreateLogicBlockConfigurationBuilder(Dictionary<string, LogicBlockContractBase> contracts,
                                                                                           Dictionary<string, LogicSenderInterfaceBase> interfaces,
                                                                                           ServiceBinder serviceBinder,
                                                                                           IServiceProvider serviceProvider)
        {
            return new LogicBlockConfigurationBuilder(AddContract,
                                                      AddInterface,
                                                      serviceBinder,
                                                      (_, _, _) => { }, // timer callback
                                                      () => string.Empty, // get logic block id
                                                      new MockActorContext(), // Mock actor context for introspection (actual context not available during logic block inspection)
                                                      (_, _, _) => { }, // schedule timer tick
                                                      serviceProvider);

            void AddContract(string identifier, LogicBlockContractBase contract)
            {
                contracts[identifier] = contract;
            }

            void AddInterface(string identifier, LogicSenderInterfaceBase logicBlockInterface)
            {
                interfaces[identifier] = logicBlockInterface;
            }
        }

        private static void InvokeConfigureMethod(LogicBlockBase logicBlock, LogicBlockConfigurationBuilder builder)
        {
            var configureMethod = logicBlock.GetType().GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic);

            if (configureMethod == null)
            {
                throw new InvalidOperationException($"No Configure method found for logic block: {logicBlock.GetType().FullName}");
            }

            configureMethod.Invoke(logicBlock, [builder]);
        }

        private static List<LogicBlockIntrospectionResult.InterfaceInfo> GetInterfaces(Dictionary<string, LogicSenderInterfaceBase> interfaces)
        {
            return interfaces.Select(i =>
                                     {
                                         var annotations = new Dictionary<string, object>(i.Value.MetaData.Annotations);
                                         MergeContractAnnotations(i.Value.LogicInterfaceType, annotations);

                                         if (i.Value.MetaData.Dependency != null)
                                         {
                                             foreach (var kvp in i.Value.MetaData.Dependency.Annotations)
                                             {
                                                 annotations[kvp.Key] = kvp.Value;
                                             }
                                         }

                                         return new LogicBlockIntrospectionResult.InterfaceInfo
                                                {
                                                    Identifier = i.Key,
                                                    InterfaceTypeFullNames = new List<string>
                                                                             {
                                                                                 ReflectionHelper.GetDisplayFullName(i.Value.LogicInterfaceType),
                                                                             },
                                                    MatchingInterfaceTypeFullNames = new List<string>
                                                                                     {
                                                                                         ReflectionHelper.GetDisplayFullName(i.Value
                                                                                             .MatchingLogicInterfaceType),
                                                                                     },
                                                    Annotations = annotations,
                                                };
                                     })
                             .ToList();
        }

        private static void MergeContractAnnotations(Type logicInterfaceType, Dictionary<string, object> annotations)
        {
            var logicInterfaceAttr = logicInterfaceType.GetCustomAttribute<LogicInterfaceAttribute>();
            if (logicInterfaceAttr == null)
            {
                return;
            }

            var contractAttr = logicInterfaceAttr.ContractType.GetCustomAttribute<ContractAttribute>();
            if (contractAttr == null)
            {
                return;
            }

            annotations["ContractName"] = logicInterfaceAttr.ContractType.Name;

            // Determine which side this interface is on
            var interfaceName = logicInterfaceType.Name;
            var isBetweenSide = interfaceName == contractAttr.BetweenInterface;

            // Pre-resolve arrow direction per-interface
            annotations["ArrowDirection"] = ResolveArrowDirection(contractAttr.Direction, isBetweenSide);

            // Role default names — only include when set
            var thisDefaultName = isBetweenSide ? contractAttr.BetweenDefaultName : contractAttr.AndDefaultName;
            var matchingDefaultName = isBetweenSide ? contractAttr.AndDefaultName : contractAttr.BetweenDefaultName;

            if (thisDefaultName != null)
            {
                annotations["RoleDefaultName"] = thisDefaultName;
            }

            if (matchingDefaultName != null)
            {
                annotations["MatchingRoleDefaultName"] = matchingDefaultName;
            }
        }

        private static string ResolveArrowDirection(ContractDirection direction, bool isBetweenSide)
        {
            return direction switch
            {
                ContractDirection.None => "None",
                ContractDirection.Bidirectional => "Bidirectional",
                ContractDirection.BetweenToAnd => isBetweenSide ? "Outbound" : "Inbound",
                ContractDirection.AndToBetween => isBetweenSide ? "Inbound" : "Outbound",
                _ => "None",
            };
        }

        private static List<LogicBlockIntrospectionResult.ContractInfo> GetContracts(Dictionary<string, LogicBlockContractBase> contracts)
        {
            return contracts.Select(i =>
                                    {
                                        var interfaceWithAttr = i.Value
                                                                 .GetType()
                                                                 .GetInterfaces()
                                                                 .FirstOrDefault(t => t.GetCustomAttribute<ServiceProviderContractTypeAttribute>() != null);

                                        if (interfaceWithAttr == null)
                                        {
                                            throw new
                                                InvalidOperationException($"No interface with {nameof(ServiceProviderContractTypeAttribute)} found for type: {i.Value.GetType().FullName}");
                                        }

                                        var attribute = interfaceWithAttr.GetCustomAttribute<ServiceProviderContractTypeAttribute>();

                                        return new LogicBlockIntrospectionResult.ContractInfo
                                               {
                                                   Identifier = i.Key,
                                                   ContractTypeFullName = ReflectionHelper.GetDisplayFullName(interfaceWithAttr),
                                                   MatchingContractType = attribute!.ServiceProviderContractType,
                                                   Annotations = i.Value.MetaData.Annotations,
                                               };
                                    })
                            .ToList();
        }

        private static List<LogicBlockIntrospectionResult.ServiceInfo> GetServices(ServiceBinder serviceBinder)
        {
            var result = new List<LogicBlockIntrospectionResult.ServiceInfo>();

            var allServicePropertyBindings = serviceBinder.GetAllServicePropertyBindings();
            var allServiceMeasuringPointBindings = serviceBinder.GetAllServiceMeasuringPointBindings();
            var serviceIdentifiers = allServicePropertyBindings.Keys.Concat(allServiceMeasuringPointBindings.Keys).Distinct().ToList();

            foreach (var serviceIdentifier in serviceIdentifiers)
            {
                var interfaceTypeFullNames = GetServiceInterfaceTypeFullNames(allServicePropertyBindings, allServiceMeasuringPointBindings, serviceIdentifier);

                var service = new LogicBlockIntrospectionResult.ServiceInfo
                              {
                                  Identifier = serviceIdentifier,
                                  InterfaceTypeFullNames = interfaceTypeFullNames,
                                  Properties = new List<LogicBlockIntrospectionResult.ServicePropertyInfo>(),
                                  MeasuringPoints = new List<LogicBlockIntrospectionResult.ServiceMeasuringPointInfo>(),
                                  InwardRelations = new List<LogicBlockIntrospectionResult.ServiceRelationInfo>(),
                                  OutwardRelations = new List<LogicBlockIntrospectionResult.ServiceRelationInfo>(),
                              };

                // Process property bindings
                if (allServicePropertyBindings.TryGetValue(serviceIdentifier, out var propertyBindingMapOfInterface))
                {
                    ProcessBindings(propertyBindingMapOfInterface, service.Properties, ProcessPropertyBinding);
                }

                // Process measuring point bindings
                if (allServiceMeasuringPointBindings.TryGetValue(serviceIdentifier, out var measuringPointBindingMapOfInterface))
                {
                    ProcessBindings(measuringPointBindingMapOfInterface, service.MeasuringPoints, ProcessMeasuringPointBinding);
                }

                // Process relations
                var allServiceRelations = serviceBinder.GetAllServiceRelations();
                if (allServiceRelations.TryGetValue(serviceIdentifier, out var relations))
                {
                    service.InwardRelations = relations.Where(r => r.Direction == ServiceRelationDirection.Inwards)
                                                       .Select(r => new LogicBlockIntrospectionResult.ServiceRelationInfo
                                                                    {
                                                                        RelationType = r.RelationType,
                                                                        InterfaceIdentifier = r.InterfaceIdentifier,
                                                                        InterfaceTypeFullName = r.InterfaceTypeFullName,
                                                                        Annotations = r.Annotations,
                                                                    })
                                                       .ToList();

                    service.OutwardRelations = relations.Where(r => r.Direction == ServiceRelationDirection.Outwards)
                                                        .Select(r => new LogicBlockIntrospectionResult.ServiceRelationInfo
                                                                     {
                                                                         RelationType = r.RelationType,
                                                                         InterfaceIdentifier = r.InterfaceIdentifier,
                                                                         InterfaceTypeFullName = r.InterfaceTypeFullName,
                                                                         Annotations = r.Annotations,
                                                                     })
                                                        .ToList();
                }

                result.Add(service);
            }

            return result;
        }

        private static List<string> GetServiceInterfaceTypeFullNames(
            IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> allServicePropertyBindings,
            IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> allServiceMeasuringPointBindings,
            string serviceIdentifier)
        {
            var propertyInterfaces = allServicePropertyBindings.GetValueOrDefault(serviceIdentifier)?.Keys.Where(k => k != ServiceBinder.ExtraPropsKey).Select(k => k!.FullName) ??
                                     [];

            var measuringPointInterfaces = allServiceMeasuringPointBindings.GetValueOrDefault(serviceIdentifier)
                                                                           ?.Keys
                                                                           .Where(k => k != ServiceBinder.ExtraPropsKey)
                                                                           .Select(ReflectionHelper.GetDisplayFullName) ?? [];

            return propertyInterfaces.Concat(measuringPointInterfaces).Distinct().ToList();
        }

        private static void ProcessBindings<T>(IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>> bindingMapOfInterface,
                                               ICollection<T> targetCollection,
                                               Func<ServiceBinding, Type?, T> bindingProcessor)
        {
            foreach (var (serviceInterfaceType, bindingMap) in bindingMapOfInterface)
            {
                foreach (var binding in bindingMap.Values)
                {
                    var processedBinding = bindingProcessor(binding, serviceInterfaceType);
                    targetCollection.Add(processedBinding);
                }
            }
        }

        private static LogicBlockIntrospectionResult.ServicePropertyInfo ProcessPropertyBinding(ServiceBinding binding, Type? serviceInterfaceType)
        {
            if (serviceInterfaceType == ServiceBinder.ExtraPropsKey)
            {
                return ProcessExtraPropertyBinding(binding);
            }

            return ProcessInterfacePropertyBinding(binding, serviceInterfaceType!);
        }

        private static LogicBlockIntrospectionResult.ServiceMeasuringPointInfo ProcessMeasuringPointBinding(ServiceBinding binding, Type? serviceInterfaceType)
        {
            if (serviceInterfaceType == ServiceBinder.ExtraPropsKey)
            {
                return ProcessExtraMeasuringPointBinding(binding);
            }

            return ProcessInterfaceMeasuringPointBinding(binding, serviceInterfaceType!);
        }

        private static LogicBlockIntrospectionResult.ServicePropertyInfo ProcessExtraPropertyBinding(ServiceBinding binding)
        {
            var prop = binding.RootSourcePropertyInfo;
            var typeRef = TypeRefBuilder.BuildForProperty(prop);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(prop.PropertyType);
            var metadata = PropertyMetadataBuilder.Build(prop, typeRef, structFieldAnnotations);
            var (schema, presentation, runtime) = ExtractSiblings(metadata);

            return new LogicBlockIntrospectionResult.ServicePropertyInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       Schema = schema,
                       Presentation = presentation,
                       Runtime = runtime,
                   };
        }

        private static LogicBlockIntrospectionResult.ServicePropertyInfo ProcessInterfacePropertyBinding(ServiceBinding binding, Type serviceInterfaceType)
        {
            // Schema source: the interface property (defines the data contract).
            var ifaceProp = serviceInterfaceType.GetProperty(binding.ServicePropertyName)!;

            // Presentation/Runtime source: the implementing logic-block property (carries UI hints and runtime flags).
            var implProp = binding.RootSourcePropertyInfo;

            var typeRef = TypeRefBuilder.BuildForProperty(ifaceProp);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(ifaceProp.PropertyType);
            var metadata = PropertyMetadataBuilder.BuildSplit(ifaceProp, implProp, typeRef, structFieldAnnotations);
            var (schema, presentation, runtime) = ExtractSiblings(metadata);

            return new LogicBlockIntrospectionResult.ServicePropertyInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       Schema = schema,
                       Presentation = presentation,
                       Runtime = runtime,
                   };
        }

        private static LogicBlockIntrospectionResult.ServiceMeasuringPointInfo ProcessExtraMeasuringPointBinding(ServiceBinding binding)
        {
            var prop = binding.RootSourcePropertyInfo;
            var typeRef = TypeRefBuilder.BuildForProperty(prop);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(prop.PropertyType);
            var metadata = PropertyMetadataBuilder.Build(prop, typeRef, structFieldAnnotations);
            var (schema, presentation, runtime) = ExtractSiblings(metadata);

            return new LogicBlockIntrospectionResult.ServiceMeasuringPointInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       Schema = schema,
                       Presentation = presentation,
                       Runtime = runtime,
                   };
        }

        private static LogicBlockIntrospectionResult.ServiceMeasuringPointInfo ProcessInterfaceMeasuringPointBinding(ServiceBinding binding, Type serviceInterfaceType)
        {
            // Schema source: the interface property.
            var ifaceProp = serviceInterfaceType.GetProperty(binding.ServicePropertyName)!;

            // Presentation/Runtime source: the implementing logic-block property.
            var implProp = binding.RootSourcePropertyInfo;

            var typeRef = TypeRefBuilder.BuildForProperty(ifaceProp);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(ifaceProp.PropertyType);
            var metadata = PropertyMetadataBuilder.BuildSplit(ifaceProp, implProp, typeRef, structFieldAnnotations);
            var (schema, presentation, runtime) = ExtractSiblings(metadata);

            return new LogicBlockIntrospectionResult.ServiceMeasuringPointInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       Schema = schema,
                       Presentation = presentation,
                       Runtime = runtime,
                   };
        }

        /// <summary>
        ///     Serializes a <see cref="PropertyMetadata" /> document and extracts the three sibling
        ///     JSON nodes — <c>schema</c>, <c>presentation</c>, <c>runtime</c> — as independent
        ///     <see cref="JsonNode" /> instances suitable for assignment to the introspection result DTO.
        ///     Each node is deep-cloned so it has no parent and can be safely reparented.
        /// </summary>
        private static (JsonNode schema, JsonNode? presentation, JsonNode? runtime) ExtractSiblings(PropertyMetadata metadata)
        {
            var fullDoc = (JsonObject)metadata.ToJson();

            // schema is always present — introspection contract requires it.
            var schema = fullDoc["schema"]!.DeepClone();

            // presentation / runtime: null when the sibling was serialized as JSON null.
            var presentationNode = fullDoc["presentation"];
            var presentation = presentationNode is null ? null : presentationNode.DeepClone();

            var runtimeNode = fullDoc["runtime"];
            var runtime = runtimeNode is null ? null : runtimeNode.DeepClone();

            return (schema, presentation, runtime);
        }

        private static Dictionary<string, object> GetLogicBlockAnnotations(Type logicBlockType)
        {
            var logicBlockInfoAttribute = logicBlockType.GetCustomAttribute<LogicBlockInfoAttribute>();
            return logicBlockInfoAttribute?.Annotations ?? new Dictionary<string, object>();
        }
    }
}
