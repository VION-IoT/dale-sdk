using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Vion.Dale.Cli.Models
{
    /// <summary>
    ///     DTO matching the JSON output of Vion.Dale.LogicBlockParser.
    ///     Hand-mirrors the Vion.Contracts introspection types (DalePluginInfo /
    ///     LogicBlockIntrospectionResult) rather than referencing the package: the CLI has no
    ///     Vion.* dependency, and the parser it shells out to comes from the consumer project's
    ///     SDK version — so this mirror must stay a tolerant reader across parser versions.
    ///     Every member is therefore optional (no <c>required</c>): unknown JSON members are
    ///     ignored and absent members stay default instead of failing deserialization.
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

        /// <summary>
        ///     Config-time inclusion gate: the [IncludedWhen] predicate string when the
        ///     service is gated, otherwise null.
        /// </summary>
        public string? IncludedWhen { get; set; }

        public List<string> InterfaceTypeFullNames { get; set; } = new();

        public List<ServicePropertyInfo> Properties { get; set; } = new();

        public List<ServiceMeasuringPointInfo> MeasuringPoints { get; set; } = new();

        public List<ServiceRelationInfo> InwardRelations { get; set; } = new();

        public List<ServiceRelationInfo> OutwardRelations { get; set; } = new();
    }

    public class ServicePropertyInfo
    {
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        ///     JSON Schema 2020-12 document (Dale profile) describing the property's data shape.
        ///     Null only for output from pre-schema parser versions.
        /// </summary>
        public JsonNode? Schema { get; set; }

        /// <summary>
        ///     Optional UI presentation hints (display name, group, ordering, etc.).
        /// </summary>
        public JsonNode? Presentation { get; set; }

        /// <summary>
        ///     Optional dale-runtime behavior hints (e.g. persistent, instantiationParameter).
        /// </summary>
        public JsonNode? Runtime { get; set; }
    }

    public class ServiceMeasuringPointInfo
    {
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        ///     JSON Schema 2020-12 document (Dale profile) describing the measuring point's data shape.
        ///     Null only for output from pre-schema parser versions.
        /// </summary>
        public JsonNode? Schema { get; set; }

        /// <summary>
        ///     Optional UI presentation hints.
        /// </summary>
        public JsonNode? Presentation { get; set; }

        /// <summary>
        ///     Optional dale-runtime behavior hints.
        /// </summary>
        public JsonNode? Runtime { get; set; }
    }

    public class ServiceRelationInfo
    {
        public string RelationType { get; set; } = string.Empty;

        public string InterfaceIdentifier { get; set; } = string.Empty;

        public string InterfaceTypeFullName { get; set; } = string.Empty;

        public Dictionary<string, object>? Annotations { get; set; }
    }
}