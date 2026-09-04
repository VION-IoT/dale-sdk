using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost
{
    internal class DevHost : IDevHost
    {
        private readonly DevConfiguration _configuration;

        private readonly List<IHostedService> _hostedServices = [];

        private readonly ILogger _logger;

        private readonly List<Assembly> _pluginAssemblies;

        private readonly IServiceProvider _serviceProvider;

        private bool _disposed;

        private bool _started;

        public DevHost(IServiceProvider serviceProvider, List<Assembly> pluginAssemblies, DevConfiguration configuration, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _pluginAssemblies = pluginAssemblies;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        public IDevHostControl Control
        {
            get => _serviceProvider.GetRequiredService<IDevHostControl>();
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            // A second start would add a second copy of every hosted service and rebind the port under the
            // running server, leaving the first one alive behind the bind failure. One host, one start.
            if (_started)
            {
                throw new InvalidOperationException("This development host is already started. Build a second host, or recycle this one through its supervisor.");
            }

            _started = true;

            _logger.LogInformation("Development host starting...");
            _logger.LogInformation("Loaded {Count} plugin assemblies:", _pluginAssemblies.Count);

            foreach (var assembly in _pluginAssemblies)
            {
                _logger.LogInformation("  - {AssemblyName}", assembly.GetName().Name);
            }

            // Introspect logic blocks and eagerly construct the control facade BEFORE starting hosted services.
            // Order matters for two reasons:
            //  1. The web server (WebHostService) serves /api/configuration as soon as it starts. If it started
            //     first, a request could race in while the introspection metadata is still empty, throwing
            //     KeyNotFoundException in DevHostIntrospection.BuildConfiguration. Introspecting first guarantees
            //     the server never serves an unintrospected host.
            //  2. The control facade must subscribe to the event stream before blocks publish, or its
            //     last-known-value cache would miss the initial state publish.
            // Introspection also assigns service ids — the single source of truth now the web state provider is
            // gone — which the initializer reads in InitializeAsync below.
            _serviceProvider.GetRequiredService<DevHostIntrospection>().EnsureIntrospected();
            _ = _serviceProvider.GetRequiredService<IDevHostControl>();

            // Start hosted services (e.g. WebHostService) — safe now that introspection has completed.
            _hostedServices.AddRange(_serviceProvider.GetServices<IHostedService>());
            foreach (var hostedService in _hostedServices)
            {
                _logger.LogDebug("Starting hosted service: {ServiceType}", hostedService.GetType().Name);
                await hostedService.StartAsync(cancellationToken);
            }

            // Get the initializer from DI
            var initializer = _serviceProvider.GetRequiredService<DevLogicSystemInitializer>();

            // Initialize the actor system and logic blocks
            _logger.LogInformation("Initializing logic system...");
            var initResult = await initializer.InitializeAsync(_configuration);

            if (!initResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to initialize logic system: {initResult.ErrorMessage}", initResult.Exception);
            }

            if (initResult.WarningMessages.Count > 0)
            {
                _logger.LogWarning("Initialization completed with {Count} warnings:", initResult.WarningMessages.Count);
                foreach (var warning in initResult.WarningMessages)
                {
                    _logger.LogWarning("  - {Warning}", warning);
                }
            }

            // Start the logic blocks
            _logger.LogInformation("Starting logic blocks...");
            await initializer.StartAsync(_configuration);

            _logger.LogInformation("Development host started successfully");
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            await StartAsync(cancellationToken);

            _logger.LogInformation("Development host running. Press Ctrl+C to exit.");

            // Wait for cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Shutdown requested");
            }

            await StopAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Development host stopping...");

            // Domain stop BEFORE the hosted services, the inverse of the original order and the mirror of the
            // runtime (block actors first, the MQTT client actor after). It matters here because the
            // StopLogicBlockRequest handler's DrainThrottlers() publishes each member's exact final value, and
            // those publishes must reach the event stream while WebHostService / SignalR is still up, so the UI
            // shows final values before a recycle instead of losing them to an already-stopped server. (The
            // handler's ClearRetainedMessages() is inert here — it raises ServicePropertyValueCleared, which no
            // DevHost mock handler observes; it clears the runtime's retained MQTT state, which DevHost has none of.)
            // The teardown cancellation token is deliberately not threaded in: RunAsync reaches here with an
            // already-cancelled token on Ctrl+C, and the stop sequence is what must still run at that point.
            // Never throws: DevLogicSystemInitializer.StopAsync downgrades every failure to a warning, and the
            // resolve itself is guarded so a host that failed before the initializer existed still tears down.
            try
            {
                await _serviceProvider.GetRequiredService<DevLogicSystemInitializer>().StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping the logic system; continuing teardown.");
            }

            // Stop hosted services
            foreach (var hostedService in _hostedServices)
            {
                _logger.LogDebug("Stopping hosted service: {ServiceType}", hostedService.GetType().Name);
                await hostedService.StopAsync(cancellationToken);
            }

            _logger.LogInformation("Development host stopped");
        }

        /// <summary>
        ///     Stops the host (idempotent) and disposes the owned service provider. Enables
        ///     <c>await using var host = …Build()</c> in tests for clean per-test teardown.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                await StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping during DisposeAsync; continuing teardown.");
            }

            // Deterministically tear down the actor system so its scheduler/threads are released now, rather
            // than lingering until GC. Important for tests that build many hosts in one process — without it,
            // accumulated actor systems contend for the thread pool and intermittently stall operations.
            try
            {
                var actorSystem = _serviceProvider.GetService<IActorSystem>();
                if (actorSystem is not null)
                {
                    await actorSystem.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error shutting down the actor system during DisposeAsync; continuing teardown.");
            }

            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}