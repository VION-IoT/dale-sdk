using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost
{
    public class DevHostBuilder
    {
        private readonly List<Assembly> _pluginAssemblies = new();

        private readonly ServiceCollection _services = new();

        private DevConfiguration? _configuration;

        private ILogger<DevHostBuilder>? _logger;

        private DevHostBuilder()
        {
        }

        public static DevHostBuilder Create()
        {
            return new DevHostBuilder();
        }

        public DevHostBuilder WithDi<TConfigureServices>()
            where TConfigureServices : IConfigureServices
        {
            var assembly = typeof(TConfigureServices).Assembly;

            if (!_pluginAssemblies.Contains(assembly))
            {
                _pluginAssemblies.Add(assembly);
            }

            return this;
        }

        public DevHostBuilder ConfigureLogging(Action<ILoggingBuilder> configure)
        {
            _services.AddLogging(configure);
            return this;
        }

        public DevHostBuilder WithConfiguration(DevConfiguration configuration)
        {
            _configuration = configuration;
            _services.AddSingleton(configuration);

            return this;
        }

        /// <summary>
        ///     Configure services in the dependency injection container
        /// </summary>
        public DevHostBuilder ConfigureServices(Action<IServiceCollection> configure)
        {
            configure(_services);
            return this;
        }

        public IDevHost Build()
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("Configuration must be provided via WithConfiguration(). " + "Use DevConfigurationBuilder to create a configuration.");
            }

            if (_services.All(s => s.ServiceType != typeof(ILoggerFactory)))
            {
                _services.AddLogging(builder =>
                                     {
                                         builder.AddConsole();
                                         builder.SetMinimumLevel(LogLevel.Debug);
                                     });
            }

            // Add Dale SDK services (required for LogicBlocks)
            _services.AddDaleSdk();

            // Register Proto.Actor system (real actor system!)
            _services.AddProtoActorSystem();

            // Register mock HAL/Service handlers
            _services.AddTransient<MockHalDigitalInputHandler>();
            _services.AddTransient<MockHalDigitalOutputHandler>();
            _services.AddTransient<MockHalAnalogInputHandler>();
            _services.AddTransient<MockHalAnalogOutputHandler>();
            _services.AddTransient<MockServicePropertyHandler>();
            _services.AddTransient<MockServiceMeasuringPointHandler>();
            _services.AddTransient<MockPersistentDataHandler>();

            // Register DevHostEvents as singleton
            _services.AddSingleton<DevHostEvents>();

            // Register initializer
            _services.AddSingleton<DevLogicSystemInitializer>();

            // Invoke IConfigureServices from plugin assemblies
            using var tempProvider = _services.BuildServiceProvider();
            _logger = tempProvider.GetRequiredService<ILogger<DevHostBuilder>>();

            foreach (var assembly in _pluginAssemblies)
            {
                InvokeConfigureServicesFromPlugin(assembly, _services, _logger);
            }

            // Build final service provider
            var serviceProvider = _services.BuildServiceProvider();
            _logger = serviceProvider.GetRequiredService<ILogger<DevHostBuilder>>();

            return new DevHost(serviceProvider, _pluginAssemblies, _configuration, _logger);
        }

        private static void InvokeConfigureServicesFromPlugin(Assembly pluginAssembly, IServiceCollection serviceCollection, ILogger logger)
        {
            var configureServicesTypes = pluginAssembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract).ToList();

            foreach (var type in configureServicesTypes)
            {
                var registration = (IConfigureServices)Activator.CreateInstance(type)!;
                registration.ConfigureServices(serviceCollection);
                logger.LogInformation("Invoked IConfigureServices from {TypeName}", type.FullName);
            }
        }
    }
}
