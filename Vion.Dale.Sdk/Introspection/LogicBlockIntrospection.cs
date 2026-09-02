using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.Conventions;
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
        // The presentation maps whose serialized key order must be canonicalized — see SortPresentationMaps.
        private static readonly string[] OrderSensitivePresentationMaps = { "statusMappings", "enumLabels" };

        public static LogicBlockIntrospectionResult IntrospectLogicBlock(LogicBlockBase logicBlock, IServiceProvider serviceProvider)
        {
            Dictionary<string, LogicBlockContractBase> contracts = new();
            Dictionary<string, LogicSenderInterfaceBase> interfaces = new();
            var serviceBinder = new ServiceBinder();

            var logicBlockSetup = CreateLogicBlockConfigurationBuilder(contracts, interfaces, serviceBinder, serviceProvider);
            InvokeConfigureMethod(logicBlock, logicBlockSetup);

            var logicBlockAnnotations = GetLogicBlockAnnotations(logicBlock.GetType());
            var naturalPositions = BuildNaturalPositionMap(logicBlock.GetType());

            return new LogicBlockIntrospectionResult
                   {
                       TypeFullName = logicBlock.GetType().FullName!,
                       Interfaces = GetInterfaces(interfaces),
                       Contracts = GetContracts(contracts),
                       Services = GetServices(serviceBinder, naturalPositions),
                       Annotations = logicBlockAnnotations,
                   };
        }

        /// <summary>
        ///     The contract bindings of <paramref name="result" /> that are declared
        ///     <see cref="ServiceProviderContractTypeAttribute.DevelopmentOnly" /> — the provider faces a
        ///     simulator binds to stand in for equipment that is not there.
        ///     <para>
        ///         A block with any such binding is development and bench surface: the production runtime
        ///         refuses to start it, and <c>dotnet pack</c> leaves it out of the introspection JSON that
        ///         travels to the cloud. The judgement is on the declaration alone — a binding gated by
        ///         <c>[IncludedWhen]</c> counts exactly like an ungated one, so a configuration cannot argue
        ///         its way past the refusal.
        ///     </para>
        /// </summary>
        public static IReadOnlyList<LogicBlockIntrospectionResult.ContractInfo> GetDevelopmentOnlyContracts(LogicBlockIntrospectionResult result)
        {
            return result.Contracts.Where(contract => contract.Annotations.TryGetValue(ServiceProviderContractAnnotations.DevelopmentOnly, out var flag) && flag is true).ToList();
        }

        /// <summary>
        ///     Walk the inheritance chain base-to-derived and assign each declared property a
        ///     monotonically increasing index. Used to sort introspection output deterministically:
        ///     base-class properties appear before derived-class properties; declaration order
        ///     within a class is preserved.
        ///     Keyed by <c>(DeclaringType, Name)</c> rather than <see cref="PropertyInfo" />
        ///     reference because reflection paths from base-class vs derived-class entry points
        ///     can yield different PropertyInfo instances for the same logical member.
        /// </summary>
        private static Dictionary<(Type DeclaringType, string Name), int> BuildNaturalPositionMap(Type logicBlockType)
        {
            var chain = new List<Type>();
            var t = (Type?)logicBlockType;
            while (t != null && t != typeof(object))
            {
                chain.Add(t);
                t = t.BaseType;
            }

            chain.Reverse(); // base-first now

            var map = new Dictionary<(Type, string), int>();
            var position = 0;
            foreach (var level in chain)
            {
                var declared = level.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var prop in declared)
                {
                    var key = (prop.DeclaringType!, prop.Name);
                    if (!map.ContainsKey(key))
                    {
                        map[key] = position++;
                    }
                }
            }

            return map;
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
                                                      serviceProvider,
                                                      BindingMode.Definition); // full member set + predicates, config-independent

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

            try
            {
                configureMethod.Invoke(logicBlock, [builder]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                // Configure is reached by reflection, so anything it throws arrives wrapped in a
                // TargetInvocationException whose own message is "Exception has been thrown by the target of
                // an invocation." — which is what `dale build` and `dotnet pack` would print for a refused
                // block instead of the reason. Rethrow the real one, stack preserved.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        private static List<LogicBlockIntrospectionResult.InterfaceInfo> GetInterfaces(Dictionary<string, LogicSenderInterfaceBase> interfaces)
        {
            return interfaces.Select(i =>
                                     {
                                         var annotations = new Dictionary<string, object>(i.Value.MetaData.Annotations);
                                         MergeContractAnnotations(i.Value.LogicInterfaceType, annotations);

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

            var contractAttr = logicInterfaceAttr.ContractType.GetCustomAttribute<LogicBlockContractAttribute>();
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

                                        var attribute = interfaceWithAttr.GetCustomAttribute<ServiceProviderContractTypeAttribute>()!;

                                        // Consumer-side Multiplicity arrives via ContractMetaData.Annotations;
                                        // provider-side Consumers is declared on the contract-type interface
                                        // and injected here (same loose-Annotations mechanism, token-valued,
                                        // emitted only when non-default).
                                        var annotations = new Dictionary<string, object>(i.Value.MetaData.Annotations);
                                        if (attribute.Consumers != LinkMultiplicity.ZeroOrMore)
                                        {
                                            annotations[LogicBlockWiringConventions.ConsumersAnnotationKey] = LinkMultiplicityWire.ToToken(attribute.Consumers);
                                        }

                                        // The handler actor that services this contract — surfaced so the DevHost
                                        // can address the generic stand-in registered under it when a scenario
                                        // drives the contract (RFC 0010).
                                        annotations[ServiceProviderContractAnnotations.ContractHandlerActorName] = i.Value.ContractHandlerActorName;

                                        // Development-only contracts (provider faces a simulator binds) are flagged
                                        // for tooling; emitted only when set, like Consumers above.
                                        if (attribute.DevelopmentOnly)
                                        {
                                            annotations[ServiceProviderContractAnnotations.DevelopmentOnly] = true;
                                        }

                                        return new LogicBlockIntrospectionResult.ContractInfo
                                               {
                                                   Identifier = i.Key,
                                                   ContractTypeFullName = ReflectionHelper.GetDisplayFullName(interfaceWithAttr),
                                                   MatchingContractType = attribute.ServiceProviderContractType,
                                                   Annotations = annotations,
                                               };
                                    })
                            .ToList();
        }

        private static List<LogicBlockIntrospectionResult.ServiceInfo> GetServices(ServiceBinder serviceBinder, Dictionary<(Type DeclaringType, string Name), int> naturalPositions)
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

                                  // The config-time inclusion predicate for a gated component service
                                  // (recorded by the service binder in Definition mode); null when unconditional.
                                  IncludedWhen = serviceBinder.GetServiceIncludedWhen(serviceIdentifier),
                              };

                // Process property bindings
                if (allServicePropertyBindings.TryGetValue(serviceIdentifier, out var propertyBindingMapOfInterface))
                {
                    ProcessBindings(propertyBindingMapOfInterface, service.Properties, ProcessPropertyBinding, naturalPositions);
                }

                // Process measuring point bindings
                if (allServiceMeasuringPointBindings.TryGetValue(serviceIdentifier, out var measuringPointBindingMapOfInterface))
                {
                    ProcessBindings(measuringPointBindingMapOfInterface, service.MeasuringPoints, ProcessMeasuringPointBinding, naturalPositions);
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
                                               Func<ServiceBinding, Type?, T> bindingProcessor,
                                               Dictionary<(Type DeclaringType, string Name), int> naturalPositions)
        {
            // Flatten bindings across all interfaces, then sort by base-to-derived natural position
            // so introspection output is deterministic regardless of reflection iteration order.
            // Properties from base classes appear before properties from derived classes; declaration
            // order within a class is preserved.
            var flat = new List<(ServiceBinding binding, Type? interfaceType, int natural)>();
            foreach (var (serviceInterfaceType, bindingMap) in bindingMapOfInterface)
            {
                foreach (var binding in bindingMap.Values)
                {
                    var prop = binding.RootSourcePropertyInfo;
                    var natural = prop.DeclaringType is { } declaringType && naturalPositions.TryGetValue((declaringType, prop.Name), out var pos) ? pos : int.MaxValue;
                    flat.Add((binding, serviceInterfaceType, natural));
                }
            }

            foreach (var (binding, interfaceType, _) in flat.OrderBy(t => t.natural))
            {
                var processedBinding = bindingProcessor(binding, interfaceType);
                targetCollection.Add(processedBinding);
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
            var metadata = PropertyMetadataBuilder.Build(prop, typeRef, structFieldAnnotations, ServiceElementStream.Property);
            var (schema, presentation, runtime) = ExtractSiblings(metadata, prop.PropertyType);
            runtime = ApplyInstantiationParameterRuntime(runtime, prop, binding, typeRef);

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
            var metadata = PropertyMetadataBuilder.BuildSplit(ifaceProp, implProp, typeRef, structFieldAnnotations, ServiceElementStream.Property);
            var (schema, presentation, runtime) = ExtractSiblings(metadata, ifaceProp.PropertyType);
            runtime = ApplyInstantiationParameterRuntime(runtime, implProp, binding, typeRef);

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
            var metadata = PropertyMetadataBuilder.Build(prop, typeRef, structFieldAnnotations, ServiceElementStream.MeasuringPoint);
            var (schema, presentation, runtime) = ExtractSiblings(metadata, prop.PropertyType);
            runtime = ApplyInstantiationParameterRuntime(runtime, prop, binding, typeRef);

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
            var metadata = PropertyMetadataBuilder.BuildSplit(ifaceProp, implProp, typeRef, structFieldAnnotations, ServiceElementStream.MeasuringPoint);
            var (schema, presentation, runtime) = ExtractSiblings(metadata, ifaceProp.PropertyType);
            runtime = ApplyInstantiationParameterRuntime(runtime, implProp, binding, typeRef);

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
        ///     <paramref name="schemaSourceType" /> is the CLR type the schema was built from — the same
        ///     type <c>TypeRefBuilder.BuildStructFieldAnnotations</c> was handed — and drives the
        ///     <c>presentation.fields</c> injection. Injecting here rather than at each caller is
        ///     deliberate: all four emission paths (plain property, measuring point, and the two
        ///     interface-bound variants) funnel through this method, so struct-field presentation
        ///     cannot go missing on one of them by omission.
        /// </summary>
        private static (JsonNode schema, JsonNode? presentation, JsonNode? runtime) ExtractSiblings(PropertyMetadata metadata, Type schemaSourceType)
        {
            var fullDoc = (JsonObject)metadata.ToJson();

            // schema is always present — introspection contract requires it.
            var schema = fullDoc["schema"]!.DeepClone();

            // presentation / runtime: null when the sibling was serialized as JSON null.
            var presentationNode = fullDoc["presentation"];
            var presentation = presentationNode is null ? null : presentationNode.DeepClone();
            presentation = ApplyStructFieldPresentation(presentation, schemaSourceType);
            SortPresentationMaps(presentation as JsonObject);

            var runtimeNode = fullDoc["runtime"];
            var runtime = runtimeNode is null ? null : runtimeNode.DeepClone();

            return (schema, presentation, runtime);
        }

        /// <summary>
        ///     Canonicalizes the key order of the two presentation maps whose serialized order is otherwise
        ///     non-reproducible. <see cref="PropertyMetadataBuilder" /> builds <c>statusMappings</c> and
        ///     <c>enumLabels</c> as immutable dictionaries, and .NET randomizes string hashing per process —
        ///     so the same assembly serialized them in a different order on every run, making
        ///     <c>dale dev --export-config</c> write a different file each time and the parser's introspection
        ///     JSON undiffable (VION-77).
        ///     Sorting happens here rather than at the export boundary so <c>Vion.Dale.LogicBlockParser</c>'s
        ///     output is canonical too. <c>schema</c> is deliberately left alone: JSON Schema has a
        ///     conventional reading order and reordering it is churn nobody asked for.
        /// </summary>
        /// <remarks>
        ///     The keys are written by <c>PropertyMetadataSerialization.ToJson</c> in <c>Vion.Contracts</c>,
        ///     which is the eventual right home for this — deferred while this repo is pinned to 3.7.0.
        /// </remarks>
        private static void SortPresentationMaps(JsonObject? presentation)
        {
            if (presentation is null)
            {
                return;
            }

            SortOrderSensitiveMaps(presentation);

            // VION-105's per-field node nests the same two maps one level down, and is itself keyed by
            // field name off an ImmutableDictionary-backed walk — so both the field keys and each field's
            // maps need the same treatment, or --export-config re-randomizes across processes again.
            if (presentation["fields"] is not JsonObject fields)
            {
                return;
            }

            var sortedFields = new JsonObject();
            foreach (var entry in fields.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                // Cloned because the value is still parented to `fields` until the assignment below replaces it.
                var field = entry.Value?.DeepClone();
                if (field is JsonObject fieldObject)
                {
                    SortOrderSensitiveMaps(fieldObject);
                }

                sortedFields[entry.Key] = field;
            }

            presentation["fields"] = sortedFields;
        }

        private static void SortOrderSensitiveMaps(JsonObject container)
        {
            foreach (var mapName in OrderSensitivePresentationMaps)
            {
                if (container[mapName] is not JsonObject map)
                {
                    continue;
                }

                var sorted = new JsonObject();
                foreach (var entry in map.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    // Cloned because the value is still parented to `map` until the assignment below replaces it.
                    sorted[entry.Key] = entry.Value?.DeepClone();
                }

                container[mapName] = sorted;
            }
        }

        /// <summary>
        ///     VION-105: augments the opaque <c>presentation</c> sibling doc with a <c>fields</c> map
        ///     carrying each struct field's authored label, enum-member labels and severities — the three
        ///     things that have no inline slot on a field's own subschema (see
        ///     <see cref="StructFieldPresentationBuilder" /> for why). <c>presentation</c> is opaque
        ///     passthrough — cloud-api stores and serves it as a bare <c>JsonNode?</c> and the dale runtime
        ///     never parses it — so the key rides it without a <c>Vion.Contracts</c> model change, mirroring
        ///     <see cref="ApplyInstantiationParameterRuntime" /> on <c>runtime</c>. Returns the node
        ///     untouched (<c>null</c> included) for a property with no struct-field presentation to carry,
        ///     so an otherwise-empty <c>presentation</c> still serializes to JSON null.
        /// </summary>
        private static JsonNode? ApplyStructFieldPresentation(JsonNode? presentation, Type schemaSourceType)
        {
            var fields = StructFieldPresentationBuilder.Build(schemaSourceType);
            if (fields is null)
            {
                return presentation;
            }

            var presentationObject = presentation as JsonObject ?? new JsonObject();
            presentationObject["fields"] = fields;

            return presentationObject;
        }

        /// <summary>
        ///     For an <c>[InstantiationParameter]</c> property, augments the opaque
        ///     <c>runtime</c> sibling doc with <c>instantiationParameter: true</c> and <c>default</c>
        ///     (the default-instance's value, JSON-scalar-encoded via
        ///     <see cref="PropertyValueCodec.ClrToJson" /> — enum member-name strings, integers as numbers).
        ///     The runtime doc is opaque passthrough (codec and mesh never read it), so these keys ride it
        ///     without a contracts-model change — mirroring the <c>Consumers</c>-annotation injection. Returns
        ///     the runtime node unchanged for ordinary properties.
        /// </summary>
        private static JsonNode? ApplyInstantiationParameterRuntime(JsonNode? runtime, PropertyInfo attributeSource, ServiceBinding binding, TypeRef typeRef)
        {
            if (attributeSource.GetCustomAttribute<InstantiationParameterAttribute>() is null)
            {
                return runtime;
            }

            var runtimeObject = runtime as JsonObject ?? new JsonObject();
            runtimeObject["instantiationParameter"] = JsonValue.Create(true);

            var value = binding.Getter(binding.Source);
            runtimeObject["default"] = value is null ? null : PropertyValueCodec.ClrToJson(value, typeRef);

            return runtimeObject;
        }

        private static Dictionary<string, object> GetLogicBlockAnnotations(Type logicBlockType)
        {
            var logicBlockAttribute = logicBlockType.GetCustomAttribute<LogicBlockAttribute>();
            var annotations = new Dictionary<string, object>();
            if (logicBlockAttribute is null)
            {
                return annotations;
            }

            // Preserve the historical "DefaultName" key for downstream consumers (dashboard / cloud-api)
            // until they migrate to "Name" in PR 5+. The attribute field is named Name; the wire key is unchanged.
            if (!string.IsNullOrEmpty(logicBlockAttribute.Name))
            {
                annotations["DefaultName"] = logicBlockAttribute.Name!;
            }

            if (!string.IsNullOrEmpty(logicBlockAttribute.Icon))
            {
                annotations["Icon"] = logicBlockAttribute.Icon!;
            }

            if (logicBlockAttribute.Groups is { Length: > 0 } groups)
            {
                annotations["Groups"] = groups;
            }

            return annotations;
        }
    }
}