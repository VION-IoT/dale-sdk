using System;
using System.IO;
using Vion.Dale.DevHost;
using Vion.Dale.DevHost.Xunit;

namespace Vion.Dale.DevHost.Xunit.Test
{
    /// <summary>
    ///     A <see cref="DevHostScenarioFixture" /> over the committed SmokeHost block catalog — the in-repo stand-in
    ///     for a consumer's fixture. Its only job is to name the SmokeHost's <c>DependencyInjection</c>.
    /// </summary>
    internal sealed class SmokeScenarioFixture : DevHostScenarioFixture
    {
        protected override DevHostBuilder ConfigureDi(DevHostBuilder builder)
        {
            return builder.WithDi<SmokeHost.DependencyInjection>();
        }
    }

    /// <summary>The SmokeHost's scenarios/topologies, copied next to the test assembly (see the .csproj).</summary>
    internal static class SmokeData
    {
        public static string ScenariosDir
        {
            get => Path.Combine(AppContext.BaseDirectory, "scenarios");
        }

        public static string TopologiesDir
        {
            get => Path.Combine(AppContext.BaseDirectory, "topologies");
        }
    }
}