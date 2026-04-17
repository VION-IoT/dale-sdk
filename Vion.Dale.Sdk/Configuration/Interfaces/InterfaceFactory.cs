using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public class InterfaceFactory : IInterfaceFactory
    {
        private readonly IActorContext _actorContext;

        private readonly Action<string, LogicSenderInterfaceBase> _addInterface;

        private readonly ILoggerFactory _loggerFactory;

        private readonly Func<LogicBlockId> _logicBlockId;

        public InterfaceFactory(Action<string, LogicSenderInterfaceBase> addInterface, Func<LogicBlockId> logicBlockId, IActorContext actorContext, ILoggerFactory loggerFactory)
        {
            _addInterface = addInterface;
            _logicBlockId = logicBlockId;
            _actorContext = actorContext;
            _loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        public TInterface Create<TInterface, TImplementation>(string identifier, TImplementation implementation)
        {
            var implementingType = GetImplementingType<TInterface>();
            var instance = InstantiateImplementingType<TInterface, TImplementation>(identifier, implementation, implementingType);

            _addInterface.Invoke(identifier, (instance as LogicSenderInterfaceBase)!);

            RegisterExtensionMethods(instance, implementation);
            return instance;
        }

        private static void RegisterExtensionMethods<TInterface, TImplementation>(TInterface interfaceInstance, TImplementation implementation)
        {
            var implementationType = typeof(TImplementation);

            // Look for the extension method in the same namespace as the implementation type
            var extensionTypeName = $"{implementationType.Namespace}.{implementationType.Name}Extensions";

            // Search through all loaded assemblies for the extension type
            var extensionType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(extensionTypeName)).FirstOrDefault(t => t != null);

            if (extensionType == null)
            {
                return;
            }

            // Use reflection to call the RegisterInstance extension
            var registerMethod = extensionType.GetMethod("RegisterInstance", BindingFlags.NonPublic | BindingFlags.Static);
            if (registerMethod == null)
            {
                throw new InvalidOperationException("RegisterInstance method not found on extension type");
            }

            registerMethod.Invoke(null, [implementation, interfaceInstance]);
        }

        private static Type GetImplementingType<TInterface>()
        {
            // get type implementing TInterface and deriving from LogicInterfaceBase
            var implementingTypes = AppDomain.CurrentDomain
                                             .GetAssemblies()
                                             .SelectMany(a =>
                                                         {
                                                             try
                                                             {
                                                                 return a.GetTypes();
                                                             }
                                                             catch (ReflectionTypeLoadException ex)
                                                             {
                                                                 // Return only the types that loaded successfully
                                                                 // This handles assemblies with missing compile-time dependencies (e.g., Roslyn)
                                                                 return ex.Types.Where(t => t != null)!;
                                                             }
                                                         })
                                             .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericType: false } && typeof(TInterface).IsAssignableFrom(t) &&
                                                         typeof(LogicSenderInterfaceBase).IsAssignableFrom(t))
                                             .ToList();

            switch (implementingTypes.Count)
            {
                case 0: throw new InvalidOperationException($"No valid implementation found for interface {typeof(TInterface).FullName}.");
                case > 1:
                    throw new
                        InvalidOperationException($"More that one valid implementation found for interface {typeof(TInterface).FullName}: {string.Join(",", implementingTypes.Select(t => t.FullName))}");
            }

            return implementingTypes.Single();
        }

        private TInterface InstantiateImplementingType<TInterface, TImplementation>(string identifier, TImplementation implementation, Type implementingType)
        {
            // conventional constructor signature expected (these classes are generated by the LogicClassGenerator):
            var instance = (TInterface)Activator.CreateInstance(implementingType,
                                                                identifier,
                                                                implementation,
                                                                _logicBlockId,
                                                                _actorContext,
                                                                _loggerFactory.CreateLogger(implementingType));
            return instance;
        }
    }
}