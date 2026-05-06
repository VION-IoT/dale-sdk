using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Fluent test builder to initialize LogicBlock instances for unit tests.
    /// </summary>
    [PublicApi]
    public class LogicBlockTestContextBuilder<TLogicBlock>
        where TLogicBlock : LogicBlockBase
    {
        private readonly TLogicBlock _logicBlock;

        private readonly LogicBlockTestContext<TLogicBlock> _logicBlockTestContext = new();

        private readonly Dictionary<Type, (ILogicHandlerInterface Instance, List<InterfaceId> Mappings)> _logicInterfaceMappings = [];

        private readonly List<(string PropertyName, object? Value, Type ValueType)> _persistentValues = [];

        private readonly List<Action<IServiceCollection>> _serviceConfigurators = [];

        private bool _autoStart = true;

        private IServiceProvider? _serviceProvider;

        public LogicBlockTestContextBuilder(TLogicBlock logicBlock)
        {
            _logicBlock = logicBlock ?? throw new ArgumentNullException(nameof(logicBlock));
        }

        /// <summary>
        ///     Adds a mapping to another logic block using a specific (self or delegated) implementation of the interface.
        /// </summary>
        public LogicBlockTestContextBuilder<TLogicBlock> WithLogicInterfaceMapping<TInterface>(Func<TLogicBlock, TInterface> instance, InterfaceId mappedInstance)
            where TInterface : ILogicHandlerInterface
        {
            var interfaceType = typeof(TInterface);
            var implementationInstance = instance(_logicBlock);
            if (!_logicInterfaceMappings.ContainsKey(interfaceType))
            {
                _logicInterfaceMappings[interfaceType] = (implementationInstance, []);
            }

            _logicInterfaceMappings[interfaceType].Mappings.Add(mappedInstance);
            return this;
        }

        /// <summary>
        ///     Adds a mapping to another logic block using the logic block's own implementation of the interface.
        /// </summary>
        public LogicBlockTestContextBuilder<TLogicBlock> WithLogicInterfaceMapping<TInterface>(InterfaceId mappedInstance)
            where TInterface : class, ILogicHandlerInterface
        {
            if (_logicBlock is not TInterface impl)
            {
                throw new InvalidOperationException($"Logic block '{typeof(TLogicBlock).FullName}' does not implement interface '{typeof(TInterface).FullName}'. " +
                                                    "Use WithLogicInterfaceMapping(Func<TLogicBlock, TInterface>, InterfaceId) to provide the exact implementation instance.");
            }

            return WithLogicInterfaceMapping(_ => impl, mappedInstance);
        }

        /// <summary>
        ///     Registers a persistent value to be restored after initialization, simulating a restart with previously saved state.
        ///     <code>
        ///     var testContext = block.CreateTestContext()
        ///         .WithPersistentValue(lb => lb.MaxPower, 42.0)
        ///         .WithPersistentValue(lb => lb.Mode, OperatingMode.Manual)
        ///         .Build();
        ///     </code>
        /// </summary>
        public LogicBlockTestContextBuilder<TLogicBlock> WithPersistentValue<TValue>(Expression<Func<TLogicBlock, TValue>> propertySelector, TValue value)
        {
            var propertyName = GetPropertyName(propertySelector);
            _persistentValues.Add((propertyName, value, typeof(TValue)));
            return this;
        }

        /// <summary>
        ///     Registers additional DI services for logic block initialization.
        ///     Use this to register services required by contract types (e.g. Modbus RTU).
        /// </summary>
        public LogicBlockTestContextBuilder<TLogicBlock> WithServices(Action<IServiceCollection> configure)
        {
            _serviceConfigurators.Add(configure);
            return this;
        }

        /// <summary>
        ///     Prevents the logic block from being started after initialization.
        ///     By default, the builder starts the block so that service property changes produce messages.
        ///     Use this when testing initialization or pre-start behavior.
        /// </summary>
        public LogicBlockTestContextBuilder<TLogicBlock> WithoutAutoStart()
        {
            _autoStart = false;
            return this;
        }

        /// <summary>
        ///     Initialize the logic block and apply any linked interfaces mapping.
        ///     After this returns the logic block's Configure(...), Ready(), and Starting() will have been executed
        ///     and the block is ready to process messages. Use <see cref="WithoutAutoStart" /> to skip starting.
        /// </summary>
        public LogicBlockTestContext<TLogicBlock> Build()
        {
            InitializeLogicBlock();
            RestorePersistentState();
            SetLinkedInterfaces();
            StartLogicBlock();
            return _logicBlockTestContext;
        }

        /// <summary>
        ///     Sends the InitializeLogicBlock message to the logic block to initialize it.
        ///     Auto-discovers service identifiers from [Service] attributes so that service property
        ///     and measuring point changes are routed correctly when the block is started.
        /// </summary>
        private void InitializeLogicBlock()
        {
            _serviceProvider ??= BuildServiceProvider();
            var serviceIdLookup = DiscoverServiceIds();
            var contractIdLookup = DiscoverContractIds();
            var initializeLogicBlock = new InitializeLogicBlock(Constants.LogicBlockId, Constants.LogicBlockName, serviceIdLookup, contractIdLookup, _serviceProvider);
            _logicBlock.HandleMessageAsync(initializeLogicBlock, _logicBlockTestContext).GetAwaiter().GetResult();
        }

        private IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddTransient<IDateTimeProvider, DateTimeProvider>();
            RegisterContractAssemblyServices(services);
            foreach (var configure in _serviceConfigurators)
            {
                configure(services);
            }

            return services.BuildServiceProvider();
        }

        /// <summary>
        ///     Auto-discovers contract properties on the logic block, finds <see cref="IConfigureServices" /> implementations
        ///     in each contract assembly, and invokes them. Mirrors what the full Dale runtime does with shared assembly
        ///     discovery.
        /// </summary>
        private static void RegisterContractAssemblyServices(IServiceCollection services)
        {
            var discoveredAssemblies = new HashSet<Assembly>();
            var type = typeof(TLogicBlock);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (property.PropertyType.GetCustomAttribute<ServiceProviderContractTypeAttribute>() == null || !property.CanWrite)
                {
                    continue;
                }

                var contractAssembly = property.PropertyType.Assembly;
                if (!discoveredAssemblies.Add(contractAssembly))
                {
                    continue;
                }

                foreach (var diType in contractAssembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    var registration = (IConfigureServices)Activator.CreateInstance(diType)!;
                    registration.ConfigureServices(services);
                }
            }
        }

        /// <summary>
        ///     Discovers contract identifiers from properties whose type has [ServiceProviderContractType].
        ///     Generates a LogicBlockContractId for each so that contracts are fully initialized in tests.
        /// </summary>
        private static Dictionary<string, LogicBlockContractId> DiscoverContractIds()
        {
            var type = typeof(TLogicBlock);
            var lookup = new Dictionary<string, LogicBlockContractId>();

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (property.PropertyType.GetCustomAttribute<ServiceProviderContractTypeAttribute>() == null || !property.CanWrite)
                {
                    continue;
                }

                var contractAttr = property.GetCustomAttribute<ServiceProviderContractAttribute>();
                var identifier = contractAttr?.Identifier ?? property.Name;
                lookup[identifier] = new LogicBlockContractId(Constants.LogicBlockId, identifier);
            }

            return lookup;
        }

        /// <summary>
        ///     Discovers service identifiers from [Service] attributes on the logic block class and its properties.
        /// </summary>
        private static Dictionary<string, ServiceIdentifier> DiscoverServiceIds()
        {
            var type = typeof(TLogicBlock);
            var lookup = new Dictionary<string, ServiceIdentifier>();

            // Class-level [Service] attributes
            var classServiceAttrs = type.GetCustomAttributes<ServiceAttribute>().ToList();
            if (classServiceAttrs.Count == 0)
            {
                classServiceAttrs.Add(new ServiceAttribute());
            }

            foreach (var attr in classServiceAttrs)
            {
                var id = string.IsNullOrEmpty(attr.Identifier) ? type.Name : attr.Identifier;
                lookup[id] = new ServiceIdentifier(id);
            }

            // Property-level [Service] attributes
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var serviceAttr = prop.GetCustomAttribute<ServiceAttribute>();
                if (serviceAttr != null)
                {
                    var propId = string.IsNullOrEmpty(serviceAttr.Identifier) ? prop.Name : serviceAttr.Identifier;
                    if (!lookup.ContainsKey(propId))
                    {
                        lookup[propId] = new ServiceIdentifier(propId);
                    }
                }
            }

            return lookup;
        }

        /// <summary>
        ///     If persistent values were registered, resolves their persistence keys and sends a RestorePersistentDataRequest.
        /// </summary>
        private void RestorePersistentState()
        {
            if (_persistentValues.Count == 0)
            {
                return;
            }

            var entries = new List<PersistentDataEntry>();

            foreach (var (propertyName, value, valueType) in _persistentValues)
            {
                var key = ResolvePersistenceKey(propertyName);
                var storedValue = value;
                var typeFullName = valueType.FullName!;

                // Convert enum to int for consistent storage (same as the real persistence system)
                if (storedValue != null && valueType.IsEnum)
                {
                    storedValue = Convert.ToInt32(storedValue);
                    typeFullName = typeof(int).FullName!;
                }

                entries.Add(new PersistentDataEntry(key, typeFullName, storedValue!));
            }

            var request = new RestorePersistentDataRequest(entries);
            _logicBlock.HandleMessageAsync(request, _logicBlockTestContext).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Resolves a C# property name to its persistence key by searching the service binder's bindings.
        ///     Falls back to the opt-in key format (_direct.{PropertyName}) if not found as a service property.
        /// </summary>
        private string ResolvePersistenceKey(string propertyName)
        {
            var serviceBinder = _logicBlock.GetPrivateField<ServiceBinder>("_serviceBinder");
            if (serviceBinder != null)
            {
                var allBindings = serviceBinder.GetAllServicePropertyBindings();
                foreach (var (serviceIdentifier, interfaceMap) in allBindings)
                {
                    foreach (var (_, bindings) in interfaceMap)
                    {
                        if (bindings.ContainsKey(propertyName))
                        {
                            return $"{serviceIdentifier}.{propertyName}";
                        }
                    }
                }
            }

            // Fall back to opt-in property key format
            return $"_direct.{propertyName}";
        }

        /// <summary>
        ///     If auto-start was requested, sends StartLogicBlockRequest and clears the infrastructure messages
        ///     produced during startup (initial state publishes, periodic save scheduling).
        /// </summary>
        private void StartLogicBlock()
        {
            if (!_autoStart)
            {
                return;
            }

            _logicBlock.HandleMessageAsync(new StartLogicBlockRequest(), _logicBlockTestContext).GetAwaiter().GetResult();
            _logicBlockTestContext.ClearRecordedMessages();
        }

        /// <summary>
        ///     Sets the linked interfaces on the logic block based on the configured mappings with the help of some reflection.
        /// </summary>
        private void SetLinkedInterfaces()
        {
            var interfacesDict = _logicBlock.GetPrivateField<Dictionary<string, LogicSenderInterfaceBase>>("_interfaces")!;
            if (interfacesDict == null)
            {
                throw new InvalidOperationException("Could not find _interfaces field on LogicBlockBase");
            }

            var linkedInterfaces = new Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>>();
            foreach (var (interfaceType, (_, mappings)) in _logicInterfaceMappings)
            {
                // Find the sender interface whose LogicInterfaceType matches the requested interface type.
                // Use IsAssignableFrom because TInterface may be the concrete type (e.g. Ping)
                // when the user writes .WithLogicInterfaceMapping(lb => lb, id), while
                // LogicInterfaceType is the contract interface (e.g. IPing).
                var senderInterface = interfacesDict.Values.FirstOrDefault(si => si.LogicInterfaceType.IsAssignableFrom(interfaceType));
                if (senderInterface == null)
                {
                    throw new InvalidOperationException($"No sender interface found for '{interfaceType.Name}'. " + "Ensure the logic block declares this interface.");
                }

                var interfaceId = new InterfaceId(Constants.LogicBlockId, senderInterface.Identifier);
                linkedInterfaces[interfaceId] = new Dictionary<InterfaceId, IActorReference>();

                foreach (var mapping in mappings)
                {
                    linkedInterfaces[interfaceId][mapping] = new TestActorReference($"mapped-logic-block-{mapping.LogicBlockId}");
                }
            }

            var setLinkedInterfaces = new SetLinkedInterfaces(linkedInterfaces);
            _logicBlock.HandleMessageAsync(setLinkedInterfaces, _logicBlockTestContext).GetAwaiter().GetResult();
        }

        private static string GetPropertyName<TValue>(Expression<Func<TLogicBlock, TValue>> propertySelector)
        {
            var expression = propertySelector.Body;

            // Unwrap Convert node added by boxing for value types
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            {
                expression = unary.Operand;
            }

            if (expression is MemberExpression { Member: PropertyInfo property })
            {
                return property.Name;
            }

            throw new ArgumentException("Expression must be a property access, e.g. lb => lb.MyProperty", nameof(propertySelector));
        }
    }
}
