using System;
using System.Collections.Generic;
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
        ///     Share one mock service provider endpoint between two contracts
        /// </summary>
        public DevConfigurationBuilder ShareContract(LogicBlockHandle lb1, string contractId1, LogicBlockHandle lb2, string contractId2)
        {
            _sharedContracts.Add((lb1, contractId1, lb2, contractId2));
            return this;
        }

        public DevConfiguration Build()
        {
            var config = new DevConfiguration();

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

        private LogicBlockHandle CreateHandle<T>(string? name, string? id)
            where T : LogicBlockBase
        {
            var actualId = id ?? $"lb_{_logicBlockCounter++}";
            var actualName = name ?? typeof(T).Name;
            return new LogicBlockHandle(actualId, actualName, typeof(T));
        }

        private void AddInterfaceMappings(DevConfiguration config, LogicBlockHandle source, LogicBlockHandle target)
        {
            var matches = DiscoverMatchingInterfaces(source.LogicBlockType, target.LogicBlockType);

            foreach (var (sourceInterfaceId, targetInterfaceId) in matches)
            {
                config.InterfaceMappings.Add(new DevInterfaceMapping
                                             {
                                                 SourceLogicBlockId = source.Id,
                                                 SourceLogicBlockName = source.Name,
                                                 SourceInterfaceIdentifier = sourceInterfaceId,
                                                 TargetLogicBlockId = target.Id,
                                                 TargetLogicBlockName = target.Name,
                                                 TargetInterfaceIdentifier = targetInterfaceId,
                                             });
            }
        }

        private void AutoConnectInterfaces(DevConfiguration config)
        {
            for (var i = 0; i < _handles.Count; i++)
            {
                for (var j = i + 1; j < _handles.Count; j++)
                {
                    AddInterfaceMappings(config, _handles[i], _handles[j]);
                }
            }
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

        private static List<(Type InterfaceType, string Identifier)> GetAllLogicInterfaces(Type type)
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

        private static List<(string Identifier, string ContractType)> GetContractProperties(Type type)
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
    }
}