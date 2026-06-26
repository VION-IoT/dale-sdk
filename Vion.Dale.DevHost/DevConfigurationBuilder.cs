using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Fluent builder for creating development configurations.
    ///     Auto-discovers contracts and interfaces via reflection.
    /// </summary>
    public class DevConfigurationBuilder
    {
        private readonly List<(LogicBlockHandle Source, LogicBlockHandle Target)> _connections = new();

        private readonly List<LogicBlockHandle> _handles = new();

        private readonly List<(LogicBlockHandle Lb1, string ContractId1, LogicBlockHandle Lb2, string ContractId2)> _sharedContracts = new();

        private bool _autoConnect;

        private int _logicBlockCounter;

        private string? _scenariosPath;

        private string? _topologiesPath;

        private string? _topologyName;

        public static DevConfigurationBuilder Create()
        {
            return new DevConfigurationBuilder();
        }

        /// <summary>
        ///     Add a LogicBlock instance to the configuration (no handle returned)
        /// </summary>
        public DevConfigurationBuilder AddLogicBlock<TLogicBlock>(string? name = null, string? id = null)
            where TLogicBlock : LogicBlockBase
        {
            _handles.Add(CreateHandle<TLogicBlock>(name, id));
            return this;
        }

        /// <summary>
        ///     Add a LogicBlock instance and return a handle for Connect() calls
        /// </summary>
        public DevConfigurationBuilder AddLogicBlock<TLogicBlock>(out LogicBlockHandle handle, string? name = null, string? id = null)
            where TLogicBlock : LogicBlockBase
        {
            handle = CreateHandle<TLogicBlock>(name, id);
            _handles.Add(handle);
            return this;
        }

        /// <summary>
        ///     Add a LogicBlock instance by runtime <see cref="Type" /> — the topology-file loader's entry
        ///     (RFC 0006 R5), where types come from JSON rather than generics.
        /// </summary>
        public DevConfigurationBuilder AddLogicBlock(Type logicBlockType, out LogicBlockHandle handle, string? name = null, string? id = null)
        {
            if (!typeof(LogicBlockBase).IsAssignableFrom(logicBlockType))
            {
                throw new ArgumentException($"{logicBlockType.FullName} is not a LogicBlockBase.", nameof(logicBlockType));
            }

            handle = new LogicBlockHandle(id ?? $"lb_{_logicBlockCounter++}", name ?? logicBlockType.Name, logicBlockType);
            _handles.Add(handle);
            return this;
        }

        /// <summary>
        ///     Add a LogicBlock instance with a display name and return a handle
        /// </summary>
        public DevConfigurationBuilder AddLogicBlock<TLogicBlock>(string name, out LogicBlockHandle handle, string? id = null)
            where TLogicBlock : LogicBlockBase
        {
            handle = CreateHandle<TLogicBlock>(name, id);
            _handles.Add(handle);
            return this;
        }

        /// <summary>
        ///     Wire all matching interface pairs between two logic blocks
        /// </summary>
        public DevConfigurationBuilder Connect(LogicBlockHandle source, LogicBlockHandle target)
        {
            _connections.Add((source, target));
            return this;
        }

        /// <summary>
        ///     Flag: auto-wire unambiguous interface matches between all block pairs in Build()
        /// </summary>
        public DevConfigurationBuilder AutoConnect()
        {
            _autoConnect = true;
            return this;
        }

        /// <summary>
        ///     Name the topology this configuration represents (e.g. "EnergyManagerClosedLoop"). Surfaced in
        ///     <c>ConfigurationOutput.TopologyName</c> and the web UI header; scenario files reference it to
        ///     guard against running against the wrong preset (RFC 0006).
        /// </summary>
        public DevConfigurationBuilder WithTopologyName(string topologyName)
        {
            _topologyName = topologyName;
            return this;
        }

        /// <summary>
        ///     Override the scenario-file directory (RFC 0006). Default: <c>{current directory}/scenarios</c>.
        /// </summary>
        public DevConfigurationBuilder WithScenarios(string path)
        {
            _scenariosPath = path;
            return this;
        }

        /// <summary>
        ///     Override the topology-file directory (RFC 0006 R5). Default: <c>{current directory}/topologies</c>.
        /// </summary>
        public DevConfigurationBuilder WithTopologies(string path)
        {
            _topologiesPath = path;
            return this;
        }

        /// <summary>
        ///     Share one mock service provider endpoint between two contracts
        /// </summary>
        public DevConfigurationBuilder ShareContract(LogicBlockHandle lb1, string contractId1, LogicBlockHandle lb2, string contractId2)
        {
            _sharedContracts.Add((lb1, contractId1, lb2, contractId2));
            return this;
        }

        public DevConfiguration Build()
        {
            var config = new DevConfiguration { TopologyName = _topologyName, ScenariosPath = _scenariosPath, TopologiesPath = _topologiesPath };

            // Create logic block configs
            foreach (var handle in _handles)
            {
                config.LogicBlocks.Add(new DevLogicBlockConfig
                                       {
                                           Id = handle.Id,
                                           Name = handle.Name,
                                           LogicBlockType = handle.LogicBlockType,
                                       });
            }

            // Auto-create service providers and contract mappings
            AutoCreateServiceProviders(config);

            // Resolve explicit connections
            foreach (var (source, target) in _connections)
            {
                AddInterfaceMappings(config, source, target);
            }

            // Auto-connect if flagged
            if (_autoConnect)
            {
                AutoConnectInterfaces(config);
            }

            return config;
        }

        /// <summary>
        ///     Discover matching interface pairs between two logic block types via reflection
        /// </summary>
        internal static List<(string SourceInterfaceId, string TargetInterfaceId)> DiscoverMatchingInterfaces(Type sourceType, Type targetType)
        {
            var sourceInterfaces = GetAllLogicInterfaces(sourceType);
            var targetInterfaces = GetAllLogicInterfaces(targetType);
            var matches = new List<(string, string)>();

            foreach (var src in sourceInterfaces)
            {
                var srcAttr = src.InterfaceType.GetCustomAttribute<LogicInterfaceAttribute>();
                if (srcAttr == null)
                {
                    continue;
                }

                foreach (var tgt in targetInterfaces)
                {
                    var tgtAttr = tgt.InterfaceType.GetCustomAttribute<LogicInterfaceAttribute>();
                    if (tgtAttr == null)
                    {
                        continue;
                    }

                    if (srcAttr.MatchingInterface == tgt.InterfaceType || tgtAttr.MatchingInterface == src.InterfaceType)
                    {
                        matches.Add((src.Identifier, tgt.Identifier));
                        break;
                    }
                }
            }

            return matches;
        }

        // The consumer-side link multiplicity declared on a block type's interface binding
        // ([LogicBlockInterfaceBinding(Multiplicity = …)]), keyed by interface identifier. Class-level bindings
        // use the interface name as the identifier; property-bound interfaces use "{Property}_{Interface}".
        // Defaults to ZeroOrMore (unconstrained) when not annotated.
        internal static LinkMultiplicity MultiplicityOf(Type blockType, string interfaceIdentifier)
        {
            foreach (var attribute in blockType.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>())
            {
                if (attribute.ForInterface.Name == interfaceIdentifier)
                {
                    return attribute.Multiplicity;
                }
            }

            foreach (var property in blockType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var attribute in property.GetCustomAttributes<LogicBlockInterfaceBindingAttribute>())
                {
                    if ($"{property.Name}_{attribute.ForInterface.Name}" == interfaceIdentifier)
                    {
                        return attribute.Multiplicity;
                    }
                }
            }

            return LinkMultiplicity.ZeroOrMore;
        }

        internal static List<(Type InterfaceType, string Identifier)> GetAllLogicInterfaces(Type type)
        {
            var result = new List<(Type, string)>();

            // Class-level interfaces
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.GetCustomAttribute<LogicInterfaceAttribute>() != null)
                {
                    result.Add((iface, iface.Name));
                }
            }

            // Property-based interfaces
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                foreach (var iface in property.PropertyType.GetInterfaces())
                {
                    if (iface.GetCustomAttribute<LogicInterfaceAttribute>() != null)
                    {
                        result.Add((iface, $"{property.Name}_{iface.Name}"));
                    }
                }
            }

            return result;
        }

        // The service-provider contracts declared on a block type's writable properties, by reflection over
        // the Type (no instantiation): each property whose PropertyType carries a [ServiceProviderContractType]
        // yields (identifier, the provider-side contract-type token). The catalog DTO (RFC 0013) reuses this —
        // the token here is exactly what LogicBlockIntrospection records as ContractInfo.MatchingContractType.
        internal static List<(string Identifier, string ContractType)> GetContractProperties(Type type)
        {
            var result = new List<(string, string)>();
            var properties = ReflectionHelper.GetProperties(type, true);

            foreach (var property in properties)
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                var contractTypeAttr = property.PropertyType.GetCustomAttribute<ServiceProviderContractTypeAttribute>();
                if (contractTypeAttr == null)
                {
                    continue;
                }

                var contractAttr = property.GetCustomAttribute<ServiceProviderContractBindingAttribute>();
                var identifier = contractAttr?.Identifier ?? property.Name;

                result.Add((identifier, contractTypeAttr.ServiceProviderContractType));
            }

            return result;
        }

        private LogicBlockHandle CreateHandle<T>(string? name, string? id)
            where T : LogicBlockBase
        {
            var actualId = id ?? $"lb_{_logicBlockCounter++}";
            var actualName = name ?? typeof(T).Name;
            return new LogicBlockHandle(actualId, actualName, typeof(T));
        }

        private void AddInterfaceMappings(DevConfiguration config, LogicBlockHandle source, LogicBlockHandle target)
        {
            // Explicit connections are intentional — added verbatim (the AutoConnect conflict guard below
            // applies only to auto-wiring, never to a connection the developer asked for).
            foreach (var mapping in BuildMappings(source, target))
            {
                config.InterfaceMappings.Add(mapping);
            }
        }

        private List<DevInterfaceMapping> BuildMappings(LogicBlockHandle source, LogicBlockHandle target)
        {
            return DiscoverMatchingInterfaces(source.LogicBlockType, target.LogicBlockType)
                   .Select(m => new DevInterfaceMapping
                                {
                                    SourceLogicBlockId = source.Id,
                                    SourceLogicBlockName = source.Name,
                                    SourceInterfaceIdentifier = m.SourceInterfaceId,
                                    TargetLogicBlockId = target.Id,
                                    TargetLogicBlockName = target.Name,
                                    TargetInterfaceIdentifier = m.TargetInterfaceId,
                                })
                   .ToList();
        }

        // AutoConnect over the catalog wires only UNAMBIGUOUS interface pairs. An interface endpoint (a
        // block + interface identifier) that matches more than one counterpart block is ambiguous — auto-
        // wiring it would build a fighting network (e.g. two "commander" blocks on one device-manager
        // interface, RFC 0008 §6.3). Ambiguous endpoints are left unwired and noted; the developer resolves
        // them by wiring explicitly in a committed topology file. This makes the behaviour match the
        // method's contract ("auto-wire unambiguous interface matches").
        private void AutoConnectInterfaces(DevConfiguration config)
        {
            var candidates = new List<DevInterfaceMapping>();
            for (var i = 0; i < _handles.Count; i++)
            {
                for (var j = i + 1; j < _handles.Count; j++)
                {
                    candidates.AddRange(BuildMappings(_handles[i], _handles[j]));
                }
            }

            // Per endpoint, the distinct set of counterpart blocks it auto-matches.
            var counterparts = new Dictionary<(string Block, string Interface), HashSet<string>>();

            void Track(string block, string iface, string other)
            {
                if (!counterparts.TryGetValue((block, iface), out var set))
                {
                    counterparts[(block, iface)] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(other);
            }

            foreach (var mapping in candidates)
            {
                Track(mapping.SourceLogicBlockId, mapping.SourceInterfaceIdentifier, mapping.TargetLogicBlockId);
                Track(mapping.TargetLogicBlockId, mapping.TargetInterfaceIdentifier, mapping.SourceLogicBlockId);
            }

            // An endpoint that matches more than one counterpart is acceptable only when its binding
            // multiplicity declares it a many-side: an AGGREGATION fan-in (many sources → one aggregator, e.g.
            // many IGridSimulationParticipant → one IGridSimulationAggregator) is legitimate and should
            // auto-wire, whereas COMMAND CONTENTION (many commanders → one single-writer managed interface,
            // RFC 0008 §6.3) must be refused. The multiplicity on the ambiguous endpoint's binding is exactly
            // that distinction: OneOrMore / ZeroOrMore = "I accept many" (fan-in); ExactlyOne / ZeroOrOne =
            // "single writer" (contention). A single match is always fine (DF-19).
            bool Acceptable(string block, string iface)
            {
                if (counterparts[(block, iface)].Count <= 1)
                {
                    return true;
                }

                var multiplicity = MultiplicityOf(block, iface);
                return multiplicity == LinkMultiplicity.OneOrMore || multiplicity == LinkMultiplicity.ZeroOrMore;
            }

            foreach (var mapping in candidates)
            {
                if (Acceptable(mapping.SourceLogicBlockId, mapping.SourceInterfaceIdentifier) && Acceptable(mapping.TargetLogicBlockId, mapping.TargetInterfaceIdentifier))
                {
                    config.InterfaceMappings.Add(mapping);
                }
            }

            // Note each REFUSED ambiguous endpoint once (single-writer interface matched by many) — a skipped
            // auto-connection should be visible, not silent. A legitimately auto-wired fan-in is not noted.
            foreach (var entry in counterparts)
            {
                if (entry.Value.Count <= 1 || Acceptable(entry.Key.Block, entry.Key.Interface))
                {
                    continue;
                }

                var name = _handles.Where(h => h.Id == entry.Key.Block).Select(h => h.Name).FirstOrDefault() ?? entry.Key.Block;
                Console.WriteLine($"AutoConnect: left '{entry.Key.Interface}' on '{name}' unwired — a single-writer interface matched by {entry.Value.Count} blocks; " +
                                  "wire it explicitly in a topology file, or mark the binding OneOrMore/ZeroOrMore if it is an aggregator fan-in.");
            }
        }

        // The consumer-side link multiplicity declared on a block's interface binding
        // ([LogicBlockInterfaceBinding(Multiplicity = …)]), for the endpoint (block id + interface identifier)
        // AutoConnect keys on. Resolves the handle's type, then reads the binding via the static overload.
        // Defaults to ZeroOrMore (unconstrained) when the block is unknown or unannotated.
        private LinkMultiplicity MultiplicityOf(string blockId, string interfaceIdentifier)
        {
            var type = _handles.FirstOrDefault(h => h.Id == blockId)?.LogicBlockType;
            return type is null ? LinkMultiplicity.ZeroOrMore : MultiplicityOf(type, interfaceIdentifier);
        }

        private void AutoCreateServiceProviders(DevConfiguration config)
        {
            // Build shared contract groups
            var sharedGroups = new Dictionary<(string LbId, string ContractId), (string SpId, string SvcId)>();
            var sharedGroupCounter = 0;

            foreach (var (lb1, contractId1, lb2, contractId2) in _sharedContracts)
            {
                var key1 = (lb1.Id, contractId1);
                var key2 = (lb2.Id, contractId2);

                if (sharedGroups.TryGetValue(key1, out var existing))
                {
                    sharedGroups[key2] = existing;
                }
                else if (sharedGroups.TryGetValue(key2, out existing))
                {
                    sharedGroups[key1] = existing;
                }
                else
                {
                    var sharedSpId = $"sp_shared_{sharedGroupCounter}";
                    var sharedSvcId = $"svc_shared_{sharedGroupCounter}";
                    sharedGroupCounter++;
                    sharedGroups[key1] = (sharedSpId, sharedSvcId);
                    sharedGroups[key2] = (sharedSpId, sharedSvcId);
                }
            }

            // Track created SPs/services to avoid duplicates
            var spConfigs = new Dictionary<string, DevServiceProviderConfig>();
            var svcConfigs = new Dictionary<string, DevServiceProviderServiceConfig>();

            foreach (var lbConfig in config.LogicBlocks)
            {
                var contractProperties = GetContractProperties(lbConfig.LogicBlockType);

                if (contractProperties.Count == 0)
                {
                    continue;
                }

                var defaultSpId = $"sp_{lbConfig.Id}";
                var defaultSvcId = $"svc_{lbConfig.Id}";

                foreach (var (identifier, contractType) in contractProperties)
                {
                    var key = (lbConfig.Id, identifier);

                    string spId;
                    string svcId;

                    if (sharedGroups.TryGetValue(key, out var shared))
                    {
                        spId = shared.SpId;
                        svcId = shared.SvcId;
                    }
                    else
                    {
                        spId = defaultSpId;
                        svcId = defaultSvcId;
                    }

                    // Ensure SP config exists
                    if (!spConfigs.TryGetValue(spId, out var spConfig))
                    {
                        spConfig = new DevServiceProviderConfig { Id = spId };
                        spConfigs[spId] = spConfig;
                        config.ServiceProviders.Add(spConfig);
                    }

                    // Ensure service config exists
                    if (!svcConfigs.TryGetValue(svcId, out var svcConfig))
                    {
                        svcConfig = new DevServiceProviderServiceConfig { Identifier = svcId };
                        svcConfigs[svcId] = svcConfig;
                        spConfig.Services.Add(svcConfig);
                    }

                    // Add contract endpoint
                    svcConfig.Contracts.Add(new DevServiceProviderContractConfig
                                            {
                                                Identifier = identifier,
                                                ContractType = contractType,
                                            });

                    // Add contract mapping to LB config
                    lbConfig.ContractMappings.Add(new DevContractMapping
                                                  {
                                                      ContractIdentifier = identifier,
                                                      ServiceProviderIdentifier = spId,
                                                      ServiceIdentifier = svcId,
                                                      ContractEndpointIdentifier = identifier,
                                                  });
                }
            }
        }
    }
}