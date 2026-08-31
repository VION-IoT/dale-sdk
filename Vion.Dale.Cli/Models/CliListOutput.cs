using System.Collections.Generic;

namespace Vion.Dale.Cli.Models
{
    public class CliListOutput
    {
        public string PackageId { get; set; } = string.Empty;

        public string? Version { get; set; }

        public string? SdkVersion { get; set; }

        public List<CliLogicBlockOutput> LogicBlocks { get; set; } = new();
    }

    public class CliLogicBlockOutput
    {
        public string Name { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public List<string> Interfaces { get; set; } = new();

        public List<string> Contracts { get; set; } = new();

        /// <summary>
        ///     True when the block binds a development-only contract (a provider face a simulator binds).
        ///     Such a block is bench surface: <c>dale pack</c> leaves it out of the introspection JSON that
        ///     travels to the cloud, and the production runtime refuses to start it. Listed all the same —
        ///     it is part of the project.
        /// </summary>
        public bool DevelopmentOnly { get; set; }

        public List<CliServiceOutput> Services { get; set; } = new();
    }

    public class CliServiceOutput
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        ///     Config-time inclusion gate (RFC 0016): the [IncludedWhen] predicate when the service
        ///     is gated, otherwise null.
        /// </summary>
        public string? IncludedWhen { get; set; }

        public List<CliPropertyOutput> Properties { get; set; } = new();

        public List<CliPropertyOutput> MeasuringPoints { get; set; } = new();
    }

    public class CliPropertyOutput
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }
}