using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Vion.Dale.DevHost
{
    public class LogicBlockHandle
    {
        public string Id { get; }

        public string Name { get; }

        public Type LogicBlockType { get; }

        internal LogicBlockHandle(string id, string name, Type type)
        {
            Id = id;
            Name = name;
            LogicBlockType = type;
        }
    }

    public class DevConfiguration
    {
        /// <summary>
        ///     Optional name identifying this wired topology (the consumer's preset, e.g.
        ///     "EnergyManagerClosedLoop"). Surfaced via <c>ConfigurationOutput.TopologyName</c> so the web UI
        ///     and agents can tell which preset is running; scenario files reference it (RFC 0006).
        /// </summary>
        public string? TopologyName { get; set; }

        /// <summary>
        ///     Optional override for the scenario-file directory (RFC 0006). Defaults to
        ///     <c>{current directory}/scenarios</c> when null.
        /// </summary>
        public string? ScenariosPath { get; set; }

        /// <summary>
        ///     Optional override for the topology-file directory (RFC 0006 R5). Defaults to
        ///     <c>{current directory}/topologies</c> when null.
        /// </summary>
        public string? TopologiesPath { get; set; }

        public List<DevLogicBlockConfig> LogicBlocks { get; set; } = [];

        public List<DevServiceProviderConfig> ServiceProviders { get; set; } = [];

        public List<DevInterfaceMapping> InterfaceMappings { get; set; } = [];

        /// <summary>
        ///     RFC 0020: contract endpoints declared as one wire. Resolved at build/load time from
        ///     <c>DevConfigurationBuilder.PairContracts</c> or a topology file's <c>contractPairings</c>; the
        ///     wire-type identity check and the runtime table are built when the host loads (the handler a
        ///     contract talks to is known only once the blocks are introspected).
        /// </summary>
        public List<DevContractPairing> ContractPairings { get; set; } = [];
    }

    /// <summary>Two contract endpoints the topology declares as one wire (RFC 0020 §4.2). Symmetric.</summary>
    public class DevContractPairing
    {
        public DevContractPairingEndpoint A { get; set; } = null!;

        public DevContractPairingEndpoint B { get; set; } = null!;
    }

    /// <summary>
    ///     One side of a <see cref="DevContractPairing" />: the (block, contract) it was declared as, joined to
    ///     the endpoint triple that contract was auto-created on — what the stand-in addresses a forward with.
    /// </summary>
    public class DevContractPairingEndpoint
    {
        public string LogicBlockId { get; set; } = null!;

        public string LogicBlockName { get; set; } = null!;

        public string ContractIdentifier { get; set; } = null!;

        public string ServiceProviderIdentifier { get; set; } = null!;

        public string ServiceIdentifier { get; set; } = null!;

        public string ContractEndpointIdentifier { get; set; } = null!;
    }

    public class DevLogicBlockConfig
    {
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;

        public Type LogicBlockType { get; set; } = null!;

        public List<DevServiceConfig> Services { get; set; } = [];

        public List<DevContractMapping> ContractMappings { get; set; } = [];

        /// <summary>
        ///     RFC 0016: operator-chosen <c>[InstantiationParameter]</c> values (identifier → JSON scalar)
        ///     from the topology file, applied to the block before <c>Configure</c> so inclusion gates
        ///     resolve at bind time. Null / empty when the instance sets no parameters.
        /// </summary>
        public IReadOnlyDictionary<string, JsonNode>? InstantiationParameters { get; set; }
    }

    public class DevServiceProviderConfig
    {
        public string Id { get; set; } = null!;

        public List<DevServiceProviderServiceConfig> Services { get; set; } = [];
    }

    public class DevServiceProviderServiceConfig
    {
        public string Identifier { get; set; } = null!;

        public List<DevServiceProviderContractConfig> Contracts { get; set; } = [];
    }

    public class DevServiceProviderContractConfig
    {
        public string Identifier { get; set; } = null!;

        public string ContractType { get; set; } = null!;
    }

    public class DevServiceConfig
    {
        public string Id { get; set; } = null!;

        public string Identifier { get; set; } = null!;
    }

    public class DevContractMapping
    {
        public string ContractIdentifier { get; set; } = null!;

        public string ServiceProviderIdentifier { get; set; } = null!;

        public string ServiceIdentifier { get; set; } = null!;

        public string ContractEndpointIdentifier { get; set; } = null!;
    }

    public class DevInterfaceMapping
    {
        public string SourceLogicBlockId { get; set; } = null!;

        public string SourceLogicBlockName { get; set; } = null!;

        public string SourceInterfaceIdentifier { get; set; } = null!;

        public string TargetLogicBlockId { get; set; } = null!;

        public string TargetLogicBlockName { get; set; } = null!;

        public string TargetInterfaceIdentifier { get; set; } = null!;
    }
}