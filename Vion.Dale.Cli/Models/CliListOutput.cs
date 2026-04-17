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

        public List<CliServiceOutput> Services { get; set; } = new();
    }

    public class CliServiceOutput
    {
        public List<CliPropertyOutput> Properties { get; set; } = new();

        public List<CliPropertyOutput> MeasuringPoints { get; set; } = new();
    }

    public class CliPropertyOutput
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }
}