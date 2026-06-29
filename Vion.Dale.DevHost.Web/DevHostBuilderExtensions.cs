using System;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web
{
    public static class DevHostBuilderExtensions
    {
        /// <summary>
        ///     Add the DevHost web UI / API. When <paramref name="stepped" /> is true — or, when it is null,
        ///     the <c>DALE_DEVHOST_STEPPED</c> env var is set (i.e. <c>dale dev --stepped</c>) — the host also
        ///     boots in deterministic stepping mode, so server-side scenario runs step exactly. This is the
        ///     universal hook every web DevHost calls, so <c>--stepped</c> works without editing <c>Program.cs</c>.
        /// </summary>
        public static DevHostBuilder WithWebUi(this DevHostBuilder builder, int port = 5000, bool? stepped = null)
        {
            builder.ConfigureServices(services =>
                                      {
                                          // Register web-specific services. State/config/set all go through the
                                          // core IDevHostControl now (registered by DevHostBuilder), so there is
                                          // no web-only state provider — one abstraction, one API.
                                          services.AddSingleton<DevHostEventBroadcaster>();

                                          // Store port configuration
                                          services.AddSingleton(new WebHostConfiguration { Port = port });

                                          // The block catalog (RFC 0013 Phase 1) — every block type the WithDi<>
                                          // plugins register. Registered LAZILY (factory, not a captured value) so
                                          // the catalog is computed at resolution time, after every WithDi call has
                                          // run; capturing builder.GetBlockCatalog() eagerly here would freeze it
                                          // before later WithDi registrations and miss their blocks.
                                          services.AddSingleton(_ => new DevBlockCatalog(builder.GetBlockCatalog()));

                                          // Add hosted service to start web server
                                          services.AddHostedService<WebHostService>();
                                      });

            // Boot stepped when explicitly requested, or — when unspecified — when `dale dev --stepped` set
            // the env var. Applied before Build()'s TryAddSingleton(TimeProvider.System), so the controllable
            // clock wins and server-side scenario runs step deterministically.
            if (stepped ?? Environment.GetEnvironmentVariable(DevHostWebRunner.SteppedEnvVar) == "1")
            {
                builder.WithDeterministicStepping();
            }

            return builder;
        }
    }

    public class WebHostConfiguration
    {
        public int Port { get; set; }
    }
}