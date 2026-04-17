using System.Collections.Generic;

namespace Vion.Dale.DevHost.Web.Api.Dtos
{
    public class ConfigurationOutput
    {
        public required List<LogicBlock> LogicBlocks { get; set; }

        public required List<ServiceProvider> ServiceProviders { get; set; }

        public required List<InterfaceMapping> InterfaceMappings { get; set; }

        public class LogicBlock
        {
            public required string Id { get; set; }

            public required string Name { get; set; }

            public required List<Service> Services { get; set; }

            public required List<LogicBlockContract> Contracts { get; set; }

            public required List<ContractMapping> ContractMappings { get; set; }
        }

        public class Service
        {
            public required string Id { get; set; }

            public required string Identifier { get; set; }

            public required List<ServiceProperty> ServiceProperties { get; set; }

            public required List<ServiceMeasuringPoint> ServiceMeasuringPoints { get; set; }
        }

        public class ServiceProperty
        {
            public required string Identifier { get; set; }

            public required bool Writable { get; set; }

            public required string ServiceElementType { get; set; }

            public required Dictionary<string, object> Annotations { get; set; }
        }

        public class ServiceMeasuringPoint
        {
            public required string Identifier { get; set; }

            public required string ServiceElementType { get; set; }

            public required Dictionary<string, object> Annotations { get; set; }
        }

        public class LogicBlockContract
        {
            public required string Identifier { get; set; }

            public required string MatchingContractType { get; set; }

            public required Dictionary<string, object> Annotations { get; set; }
        }

        public class ServiceProvider
        {
            public required string Id { get; set; }

            public required List<ServiceProviderService> Services { get; set; }
        }

        public class ServiceProviderService
        {
            public required string Identifier { get; set; }

            public required List<ServiceProviderContract> Contracts { get; set; }
        }

        public class ServiceProviderContract
        {
            public required string Identifier { get; set; }

            public required string ContractType { get; set; }
        }

        public class InterfaceMapping
        {
            public required string SourceLogicBlockId { get; set; }

            public required string SourceLogicBlockName { get; set; }

            public required string SourceInterfaceIdentifier { get; set; }

            public required string TargetLogicBlockId { get; set; }

            public required string TargetLogicBlockName { get; set; }

            public required string TargetInterfaceIdentifier { get; set; }
        }

        public class ContractMapping
        {
            public required string ContractIdentifier { get; set; }

            public required string MappedServiceProviderIdentifier { get; set; }

            public required string MappedServiceIdentifier { get; set; }

            public required string MappedContractIdentifier { get; set; }
        }
    }
}