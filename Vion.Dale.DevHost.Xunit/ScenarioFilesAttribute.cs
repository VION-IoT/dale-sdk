using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Vion.Dale.DevHost.Xunit
{
    /// <summary>
    ///     An xUnit.net v3 theory data source that yields one row per committed <c>*.scenario.json</c> under the
    ///     scenarios directory (RFC 0006 R4). Each row is <c>(string id, string topology)</c>, display-named by
    ///     the scenario title, with the scenario's <c>specs</c> as <c>spec</c> traits — so each scenario is its
    ///     own entry in Test Explorer / <c>--list-tests</c>. Files that fail to parse, or declare no topology,
    ///     are skipped: catching those is the job of <c>dale scenario validate</c> in CI, not the runner.
    ///     <para>
    ///         Pair it with a <see cref="DevHostScenarioFixture" /> and
    ///         <see
    ///             cref="DevHostScenarioExtensions.RunScenarioAsync(IDevHost, string, string?, ScenarioRunOptions?, System.Threading.CancellationToken)" />
    ///         :
    ///         <code>
    /// [Theory]
    /// [ScenarioFiles]
    /// public async Task RunsGreen(string id, string topology)
    /// {
    ///     await using var host = await _fixture.LoadAsync(topology, stepped: true);
    ///     (await host.RunScenarioAsync(id)).AssertSucceeded();
    /// }
    ///         </code>
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ScenarioFilesAttribute : DataAttribute
    {
        /// <summary>
        ///     Scenarios directory to discover. Defaults to <c>{cwd}/scenarios</c> (walking up to the repo root),
        ///     matching <see cref="ScenarioStore" />.
        /// </summary>
        public string? Directory { get; set; }

        /// <summary>When set, only scenarios declaring this exact topology are yielded — the per-topology fixture case.</summary>
        public string? Topology { get; set; }

        public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
        {
            var rows = new List<ITheoryDataRow>();

            foreach (var entry in new ScenarioStore(Directory).List())
            {
                // Broken files (parse error) and topology-less files can't be run against a host — skip them.
                if (entry.Error is not null || entry.Topology is null)
                {
                    continue;
                }

                if (Topology is not null && !string.Equals(entry.Topology, Topology, StringComparison.Ordinal))
                {
                    continue;
                }

                var row = new TheoryDataRow<string, string>(entry.Id, entry.Topology).WithTestDisplayName(entry.Title ?? entry.Id);
                foreach (var spec in entry.Specs)
                {
                    row = row.WithTrait("spec", spec);
                }

                rows.Add(row);
            }

            return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
        }

        // Each scenario file is a stable, cheaply-enumerated row — surface them individually at discovery time.
        public override bool SupportsDiscoveryEnumeration()
        {
            return true;
        }
    }
}