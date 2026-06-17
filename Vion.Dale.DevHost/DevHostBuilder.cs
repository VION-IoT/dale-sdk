using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Abstractions;
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

        /// <summary>
        ///     Enumerate the <see cref="LogicBlockBase" /> types registered by all plugin assemblies added
        ///     via <see cref="WithDi{TConfigureServices}" />. Each registered concrete block type appears
        ///     exactly once (distinct by type). Does not require <see cref="Build" /> to have been called.
        ///     <para>
        ///         The generator (<c>IConfigureServices</c>) registers blocks as <c>AddTransient&lt;TBlock&gt;()</c>
        ///         — so <c>ServiceDescriptor.ServiceType</c> is the concrete block type, directly assignable
        ///         to <see cref="LogicBlockBase" />.
        ///     </para>
        /// </summary>
        public IReadOnlyList<Type> GetBlockCatalog()
        {
            var tempServices = new ServiceCollection();

            foreach (var assembly in _pluginAssemblies)
            {
                var configureServicesTypes = assembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract).ToList();

                foreach (var type in configureServicesTypes)
                {
                    var registration = (IConfigureServices)Activator.CreateInstance(type)!;
                    registration.ConfigureServices(tempServices);
                }
            }

            return tempServices.Where(sd => typeof(LogicBlockBase).IsAssignableFrom(sd.ServiceType)).Select(sd => sd.ServiceType).Distinct().ToList();
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

            // Headless control surface (RFC 0003): a log sink + ILoggerProvider that captures the
            // DevHost's log output (additive — alongside the console provider, which is unchanged), and
            // the IDevHostControl facade for tests / agents. All additive; the web UI is unaffected.
            _services.AddSingleton<DevHostLogSink>();
            _services.AddSingleton<ILoggerProvider>(sp => new DevHostLogSinkProvider(sp.GetRequiredService<DevHostLogSink>()));
            _services.AddSingleton<DevHostIntrospection>();

            // Message tap: the SAME instance is registered as both the concrete type and the opt-in
            // IActorMessageObserver the ProtoActor middleware looks up (RFC 0003). Registering the observer
            // here — only in DevHost — is what activates the tap; the production runtime registers none.
            _services.AddSingleton<MessageTap>();
            _services.AddSingleton<IActorMessageObserver>(sp => sp.GetRequiredService<MessageTap>());

            // Run control: pause gate + reset signal. Same opt-in pattern as the tap — registering the
            // IDelayedSendGate here (only in DevHost) is what enables pause; production registers none.
            _services.AddSingleton<DevHostRunControl>();
            _services.AddSingleton<IDelayedSendGate>(sp => sp.GetRequiredService<DevHostRunControl>());

            // In-flight handler monitor: the exact-quiescence complement to mailbox depth. Same opt-in
            // pattern — registering IActorActivityMonitor here (only in DevHost) is what lets the stepping
            // barrier read an EXACT quiescence predicate; the production runtime registers none.
            _services.AddSingleton<InFlightActivityMonitor>();
            _services.AddSingleton<IActorActivityMonitor>(sp => sp.GetRequiredService<InFlightActivityMonitor>());

            // Virtual schedule: the engine-owned view of pending delayed sends. Same opt-in pattern —
            // registering IVirtualSchedule here (only in DevHost) is what lets next-event stepping ask
            // "when is the next scheduled event?" (the FakeTimeProvider doesn't expose that); the
            // production runtime registers none, so SendToSelfAfter is unchanged there.
            _services.AddSingleton<VirtualSchedule>();
            _services.AddSingleton<IVirtualSchedule>(sp => sp.GetRequiredService<VirtualSchedule>());

            _services.AddSingleton<IDevHostControl, DevHostControl>();

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