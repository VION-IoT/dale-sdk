using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;
using Vion.Contracts.Constants;
using Vion.Contracts.Introspection;

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
                                  var interfaceWithAttr = i.Value.GetType().GetInterfaces().FirstOrDefault(t => t.GetCustomAttribute<ServiceProviderContractTypeAttribute>() != null);

                                  if (interfaceWithAttr == null)
                                  {
                                      throw new InvalidOperationException($"No interface with {nameof(ServiceProviderContractTypeAttribute)} found for type: {i.Value.GetType().FullName}");
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
            var logicBlockPropertyInfo = binding.RootSourcePropertyInfo;
            var servicePropertyAttribute = logicBlockPropertyInfo.GetCustomAttribute<ServicePropertyAttribute>();

            var annotations = servicePropertyAttribute?.Annotations ?? new Dictionary<string, object>();

            // Add enum values to annotations
            if (logicBlockPropertyInfo.PropertyType.IsEnum)
            {
                annotations["EnumValues"] = BuildEnumValues(logicBlockPropertyInfo.PropertyType);
            }

            MergeUiAnnotations(logicBlockPropertyInfo, annotations);

            return new LogicBlockIntrospectionResult.ServicePropertyInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       TypeFullName = ReflectionHelper.GetDisplayFullName(logicBlockPropertyInfo.PropertyType),
                       Writable = binding.Setter != null,
                       ServiceElementType = MapToServiceElementType(logicBlockPropertyInfo.PropertyType),
                       Annotations = annotations,
                   };
        }

        private static LogicBlockIntrospectionResult.ServicePropertyInfo ProcessInterfacePropertyBinding(ServiceBinding binding, Type serviceInterfaceType)
        {
            var serviceInterfacePropertyInfo = serviceInterfaceType.GetProperty(binding.ServicePropertyName)!;
            var serviceInterfacePropertyAttribute = serviceInterfacePropertyInfo.GetCustomAttribute<ServicePropertyAttribute>();
            var servicePropertyAttribute = binding.RootSourcePropertyInfo.GetCustomAttribute<ServicePropertyAttribute>();

            var annotations = MergeAnnotations(serviceInterfacePropertyAttribute?.Annotations, servicePropertyAttribute?.Annotations);

            // Add enum values to annotations
            if (serviceInterfacePropertyInfo.PropertyType.IsEnum)
            {
                annotations["EnumValues"] = BuildEnumValues(serviceInterfacePropertyInfo.PropertyType);
            }

            // UI annotations from the implementation property take precedence
            MergeUiAnnotations(binding.RootSourcePropertyInfo, annotations);

            return new LogicBlockIntrospectionResult.ServicePropertyInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       TypeFullName = ReflectionHelper.GetDisplayFullName(serviceInterfacePropertyInfo.PropertyType),
                       Writable = binding.Setter != null,
                       ServiceElementType = MapToServiceElementType(serviceInterfacePropertyInfo.PropertyType),
                       Annotations = annotations,
                   };
        }

        private static LogicBlockIntrospectionResult.ServiceMeasuringPointInfo ProcessExtraMeasuringPointBinding(ServiceBinding binding)
        {
            var logicBlockPropertyInfo = binding.RootSourcePropertyInfo;
            var serviceMeasuringPointAttribute = logicBlockPropertyInfo.GetCustomAttribute<ServiceMeasuringPointAttribute>();

            var annotations = serviceMeasuringPointAttribute?.Annotations ?? new Dictionary<string, object>();
            MergeUiAnnotations(logicBlockPropertyInfo, annotations);

            return new LogicBlockIntrospectionResult.ServiceMeasuringPointInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       TypeFullName = ReflectionHelper.GetDisplayFullName(logicBlockPropertyInfo.PropertyType),
                       ServiceElementType = MapToServiceElementType(logicBlockPropertyInfo.PropertyType),
                       Annotations = annotations,
                   };
        }

        private static LogicBlockIntrospectionResult.ServiceMeasuringPointInfo ProcessInterfaceMeasuringPointBinding(ServiceBinding binding, Type serviceInterfaceType)
        {
            var serviceInterfacePropertyInfo = serviceInterfaceType.GetProperty(binding.ServicePropertyName)!;
            var serviceInterfacePropertyAttribute = serviceInterfacePropertyInfo.GetCustomAttribute<ServiceMeasuringPointAttribute>();
            var servicePropertyAttribute = binding.RootSourcePropertyInfo.GetCustomAttribute<ServiceMeasuringPointAttribute>();

            var annotations = MergeAnnotations(serviceInterfacePropertyAttribute?.Annotations, servicePropertyAttribute?.Annotations);

            // UI annotations from the implementation property take precedence
            MergeUiAnnotations(binding.RootSourcePropertyInfo, annotations);

            return new LogicBlockIntrospectionResult.ServiceMeasuringPointInfo
                   {
                       Identifier = binding.ServicePropertyName,
                       TypeFullName = ReflectionHelper.GetDisplayFullName(serviceInterfacePropertyInfo.PropertyType),
                       ServiceElementType = MapToServiceElementType(serviceInterfacePropertyInfo.PropertyType),
                       Annotations = annotations,
                   };
        }

        private static string MapToServiceElementType(Type type)
        {
            return type switch
            {
                not null when type == typeof(bool) => ServiceElementTypes.Bool,
                not null when type == typeof(string) => ServiceElementTypes.String,
                not null when type == typeof(int) || type == typeof(long) || type == typeof(short) => ServiceElementTypes.Integer,
                not null when type == typeof(float) || type == typeof(double) || type == typeof(decimal) => ServiceElementTypes.Number,
                not null when type == typeof(DateTime) => ServiceElementTypes.DateTime,
                not null when type == typeof(TimeSpan) => ServiceElementTypes.Duration,
                not null when type.IsEnum => ServiceElementTypes.Integer, // Treat enums as integers for now
                _ => throw new NotSupportedException($"Unsupported type: {type}"),
            };
        }

        private static List<Dictionary<string, object>> BuildEnumValues(Type enumType)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var enumValueInfoAttr = field.GetCustomAttribute<EnumValueInfoAttribute>();
                var entry = new Dictionary<string, object>
                            {
                                ["Name"] = field.Name,
                                ["Value"] = (int)Enum.Parse(enumType, field.Name),
                            };

                if (enumValueInfoAttr?.DefaultName != null)
                {
                    entry["DefaultName"] = enumValueInfoAttr.DefaultName;
                }

                result.Add(entry);
            }

            return result;
        }

        private static Dictionary<string, object> GetLogicBlockAnnotations(Type logicBlockType)
        {
            var logicBlockInfoAttribute = logicBlockType.GetCustomAttribute<LogicBlockInfoAttribute>();
            return logicBlockInfoAttribute?.Annotations ?? new Dictionary<string, object>();
        }

        private static void MergeUiAnnotations(PropertyInfo propertyInfo, Dictionary<string, object> annotations)
        {
            var displayAttribute = propertyInfo.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute != null)
            {
                foreach (var kvp in displayAttribute.Annotations)
                {
                    annotations[kvp.Key] = kvp.Value;
                }

                // DisplayName takes precedence over DefaultName
                if (displayAttribute.Name != null)
                {
                    annotations["DefaultName"] = displayAttribute.Name;
                }
            }

            var categoryAttribute = propertyInfo.GetCustomAttribute<CategoryAttribute>();
            if (categoryAttribute != null)
            {
                foreach (var kvp in categoryAttribute.Annotations)
                {
                    annotations[kvp.Key] = kvp.Value;
                }
            }

            var importanceAttribute = propertyInfo.GetCustomAttribute<ImportanceAttribute>();
            if (importanceAttribute != null)
            {
                foreach (var kvp in importanceAttribute.Annotations)
                {
                    annotations[kvp.Key] = kvp.Value;
                }
            }

            var uiHintAttribute = propertyInfo.GetCustomAttribute<UIHintAttribute>();
            if (uiHintAttribute != null)
            {
                foreach (var kvp in uiHintAttribute.Annotations)
                {
                    annotations[kvp.Key] = kvp.Value;
                }
            }

            var statusIndicatorAttribute = propertyInfo.GetCustomAttribute<StatusIndicatorAttribute>();
            if (statusIndicatorAttribute != null)
            {
                foreach (var kvp in statusIndicatorAttribute.Annotations)
                {
                    annotations[kvp.Key] = kvp.Value;
                }

                // Read enum values and their severity mappings
                var enumType = propertyInfo.PropertyType;
                if (enumType.IsEnum)
                {
                    var statusMappings = new List<Dictionary<string, object>>();
                    foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        var severityAttr = field.GetCustomAttribute<StatusSeverityAttribute>();
                        var enumValueInfoAttr = field.GetCustomAttribute<EnumValueInfoAttribute>();
                        var mapping = new Dictionary<string, object>
                                      {
                                          ["Name"] = field.Name,
                                          ["Value"] = (int)Enum.Parse(enumType, field.Name),
                                          ["Severity"] = severityAttr?.Severity.ToString() ?? StatusSeverity.Neutral.ToString(),
                                      };

                        if (enumValueInfoAttr?.DefaultName != null)
                        {
                            mapping["DefaultName"] = enumValueInfoAttr.DefaultName;
                        }

                        statusMappings.Add(mapping);
                    }

                    annotations["StatusMappings"] = statusMappings;
                }
            }
        }

        private static Dictionary<string, object> MergeAnnotations(Dictionary<string, object>? baseAnnotations, Dictionary<string, object>? overrideAnnotations)
        {
            var result = new Dictionary<string, object>();

            if (baseAnnotations != null)
            {
                foreach (var kvp in baseAnnotations)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            if (overrideAnnotations != null)
            {
                foreach (var kvp in overrideAnnotations)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }
    }
}