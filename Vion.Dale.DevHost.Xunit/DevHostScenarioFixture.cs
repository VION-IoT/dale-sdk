using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Xunit
{
    /// <summary>
    ///     Base for an xUnit fixture that builds and starts a DevHost on a named topology for scenario tests.
    ///     The one consumer-specific seam is <see cref="ConfigureDi" /> — chain your block catalog's
    ///     <c>WithDi&lt;TConfigureServices&gt;()</c> calls there. Everything else (topology file load, optional
    ///     deterministic stepping clock, build, start) is handled.
    ///     <para>
    ///         Each <see cref="LoadAsync" /> / <see cref="StartHostAsync" /> returns a FRESH host the caller owns
    ///         and disposes (<c>await using</c>). One host per test row is the isolation that keeps scenarios from
    ///         interleaving on a shared network — the in-process equivalent of the runner's one-active-run-per-host
    ///         rule. A scenario only runs against the topology it declares, so build the host on that topology
    ///         (the <see cref="ScenarioFilesAttribute" /> row carries it) — a mismatch is a loud failure, not a skip.
    ///     </para>
    ///     <para>
    ///         The fixture itself holds no host, so it is safe to share via <c>IClassFixture&lt;T&gt;</c> /
    ///         <c>ICollectionFixture&lt;T&gt;</c> — it is a host factory, not a host.
    ///     </para>
    /// </summary>
    public abstract class DevHostScenarioFixture
    {
        /// <summary>Virtual-clock start for stepped hosts; <c>null</c> uses the SDK's fixed reproducible epoch.</summary>
        protected virtual DateTimeOffset? SteppedEpoch
        {
            get => null;
        }

        /// <summary>
        ///     Build and start a host on the file-backed <paramref name="topology" /> (default
        ///     <c>{cwd}/topologies</c>, walking up to the repo root). Set <paramref name="stepped" /> for a
        ///     deterministic virtual clock so <c>advance</c> / <c>settle</c> / <c>waitUntil</c> run exactly and
        ///     instantly.
        /// </summary>
        public Task<IDevHost> LoadAsync(string topology, bool stepped = false, string? topologiesDir = null, CancellationToken cancellationToken = default)
        {
            return StartHostAsync(DevTopologyLoader.Load(topology, topologiesDir), stepped, cancellationToken);
        }

        /// <summary>
        ///     Build and start a host from an already-built configuration — e.g. a C#-wired test-only topology
        ///     (<c>DevConfigurationBuilder.Create()…WithTopologyName(name).Build()</c>) that has no committed file.
        /// </summary>
        public async Task<IDevHost> StartHostAsync(DevConfiguration configuration, bool stepped = false, CancellationToken cancellationToken = default)
        {
            var builder = ConfigureDi(DevHostBuilder.Create()).WithConfiguration(configuration);
            if (stepped)
            {
                builder = builder.WithDeterministicStepping(SteppedEpoch);
            }

            var host = builder.Build();
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return host;
        }

        /// <summary>
        ///     Register the block catalog — the assemblies that declare the topology's blocks — via
        ///     <c>builder.WithDi&lt;TConfigureServices&gt;()</c> (chainable). This is the only part of host
        ///     construction the SDK cannot know; everything else is generic.
        /// </summary>
        protected abstract DevHostBuilder ConfigureDi(DevHostBuilder builder);
    }
}