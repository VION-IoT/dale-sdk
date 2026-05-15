using System;
using System.Collections.Generic;

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
        public List<DevLogicBlockConfig> LogicBlocks { get; set; } = [];

        public List<DevServiceProviderConfig> ServiceProviders { get; set; } = [];

        public List<DevInterfaceMapping> InterfaceMappings { get; set; } = [];
    }

    public class DevLogicBlockConfig
    {
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;

        public Type LogicBlockType { get; set; } = null!;

        public List<DevServiceConfig> Services { get; set; } = [];

        public List<DevContractMapping> ContractMappings { get; set; } = [];
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