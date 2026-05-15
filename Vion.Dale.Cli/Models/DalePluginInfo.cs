using System.Collections.Generic;

namespace Vion.Dale.Cli.Models
{
    /// <summary>
    ///     DTO matching the JSON output of Vion.Dale.LogicBlockParser.
    ///     Mirrors Vion.Contracts/Introspection types for deserialization.
    /// </summary>
    public class DalePluginInfo
    {
        public string PackageId { get; set; } = string.Empty;

        public string PackageVersion { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }

        public List<LogicBlockResult> LogicBlocks { get; set; } = new();
    }

    public class LogicBlockResult
    {
        public string TypeFullName { get; set; } = string.Empty;

        public List<InterfaceInfo> Interfaces { get; set; } = new();

        public List<ContractInfo> Contracts { get; set; } = new();

        public List<ServiceInfo> Services { get; set; } = new();

        public Dictionary<string, object>? Annotations { get; set; }
    }

    public class InterfaceInfo
    {
        public string Identifier { get; set; } = string.Empty;

        public List<string> InterfaceTypeFullNames { get; set; } = new();

        public List<string> MatchingInterfaceTypeFullNames { get; set; } = new();

        public Dictionary<string, object>? Annotations { get; set; }
    }

    public class ContractInfo
    {
        public string Identifier { get; set; } = string.Empty;

        public string ContractTypeFullName { get; set; } = string.Empty;

        public string MatchingContractType { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }
    }

    public class ServiceInfo
    {
        public string Identifier { get; set; } = string.Empty;

        public List<string> InterfaceTypeFullNames { get; set; } = new();

        public List<ServicePropertyInfo> Properties { get; set; } = new();

        public List<ServiceMeasuringPointInfo> MeasuringPoints { get; set; } = new();

        public List<ServiceRelationInfo> InwardRelations { get; set; } = new();

        public List<ServiceRelationInfo> OutwardRelations { get; set; } = new();
    }

    public class ServicePropertyInfo
    {
        public string Identifier { get; set; } = string.Empty;

        public string TypeFullName { get; set; } = string.Empty;

        public bool Writable { get; set; }

        public string ServiceElementType { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }
    }

    public class ServiceMeasuringPointInfo
    {
        public string Identifier { get; set; } = string.Empty;

        public string TypeFullName { get; set; } = string.Empty;

        public string ServiceElementType { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }
    }

    public class ServiceRelationInfo
    {
        public string RelationType { get; set; } = string.Empty;

        public string InterfaceIdentifier { get; set; } = string.Empty;

        public string InterfaceTypeFullName { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }
    }
}