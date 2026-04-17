using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost
{
    internal class DevHost : IDevHost
    {
        private readonly DevConfiguration _configuration;

        private readonly List<IHostedService> _hostedServices = [];

        private readonly ILogger _logger;

        private readonly List<Assembly> _pluginAssemblies;

        private readonly IServiceProvider _serviceProvider;

        public DevHost(IServiceProvider serviceProvider, List<Assembly> pluginAssemblies, DevConfiguration configuration, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _pluginAssemblies = pluginAssemblies;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Development host starting...");
            _logger.LogInformation("Loaded {Count} plugin assemblies:", _pluginAssemblies.Count);

            foreach (var assembly in _pluginAssemblies)
            {
                _logger.LogInformation("  - {AssemblyName}", assembly.GetName().Name);
            }

            // Start hosted services (e.g. WebHostService)
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

            // Stop hosted services
            foreach (var hostedService in _hostedServices)
            {
                _logger.LogDebug("Stopping hosted service: {ServiceType}", hostedService.GetType().Name);
                await hostedService.StopAsync(cancellationToken);
            }

            // TODO: Send stop messages to logic blocks
            // foreach (var logicBlockConfig in _configuration.LogicBlocks)
            // {
            //     var actorRef = _actorSystem.LookupByName(...);
            //     _actorSystem.SendTo(actorRef, new StopLogicBlockRequest());
            // }

            _logger.LogInformation("Development host stopped");
        }
    }
}