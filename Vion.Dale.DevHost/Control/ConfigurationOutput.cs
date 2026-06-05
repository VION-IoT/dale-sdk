using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Full introspection of the wired network — logic blocks, services, property/measuring-point schemas,
    ///     presentation, contracts, and wiring. Returned by <see cref="IDevHostControl.GetConfiguration" />;
    ///     the web UI renders it, and agents can read it for the heavyweight view (the lightweight topology is
    ///     <see cref="IDevHostControl.ListLogicBlocks" />).
    /// </summary>
    public class ConfigurationOutput
    {
        public required List<LogicBlock> LogicBlocks { get; set; }

        public required List<ServiceProvider> ServiceProviders { get; set; }

        public required List<InterfaceMapping> InterfaceMappings { get; set; }

        public class LogicBlock
        {
            public required string Id { get; set; }

            public required string Name { get; set; }

            /// <summary>
            ///     Block-level annotations from the introspection result — carries the <c>[LogicBlock]</c>
            ///     attribute payload (DefaultName, Icon, Groups[]) and any integrator-defined extras.
            /// </summary>
            public required Dictionary<string, object> Annotations { get; set; }

            public required List<Service> Services { get; set; }

            public required List<LogicBlockInterface> Interfaces { get; set; }

            public required List<LogicBlockContract> Contracts { get; set; }

            public required List<ContractMapping> ContractMappings { get; set; }
        }

        public class LogicBlockInterface
        {
            public required string Identifier { get; set; }

            public required Dictionary<string, object> Annotations { get; set; }
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

            /// <summary>JSON Schema 2020-12 document (Dale profile) describing the property's data shape.</summary>
            public required JsonNode Schema { get; set; }

            /// <summary>Optional UI presentation hints. Null when the property has no presentation metadata.</summary>
            public JsonNode? Presentation { get; set; }

            /// <summary>Optional dale-runtime behavior hints. Null when the property has no runtime metadata.</summary>
            public JsonNode? Runtime { get; set; }
        }

        public class ServiceMeasuringPoint
        {
            public required string Identifier { get; set; }

            /// <summary>JSON Schema 2020-12 document (Dale profile) describing the measuring point's data shape.</summary>
            public required JsonNode Schema { get; set; }

            /// <summary>Optional UI presentation hints. Null when the measuring point has no presentation metadata.</summary>
            public JsonNode? Presentation { get; set; }
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