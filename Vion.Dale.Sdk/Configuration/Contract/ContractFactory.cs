using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Reflection;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public class ContractFactory : IContractFactory
    {
        private readonly IActorContext _actorContext;

        private readonly Action<string, LogicBlockContractBase> _addContract;

        private readonly IServiceProvider _serviceProvider;

        public ContractFactory(Action<string, LogicBlockContractBase> addContract, IActorContext actorContext, IServiceProvider serviceProvider)
        {
            _addContract = addContract;
            _actorContext = actorContext;
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public object Create(Type propertyType, string identifier)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = assemblies.GetConcreteTypes(propertyType);

            // The framework's own message for an empty sequence is "Sequence contains no elements", which
            // names neither the contract nor the binding that wanted it — and this is the only frame where
            // the binding's identifier is in scope, so a refusal minted deeper cannot name it.
            var concreteType = candidates.FirstOrDefault() ??
                               throw new InvalidOperationException($"No implementation of contract '{propertyType.FullName}' is loaded, so binding '{identifier}' cannot be " +
                                                                   "constructed. Reference the package that ships the contract's implementation, or check that the plugin " +
                                                                   "carrying it was loaded.");

            if (candidates.Count > 1)
            {
                // The pick is the first the assembly walk reaches, which is not a rule an author can predict.
                // Refusing instead would fail a configuration that ships two implementations legitimately, so
                // the warning is what makes the arbitrary choice visible.
                LogAmbiguousContractImplementation(identifier, propertyType, candidates.Select(candidate => candidate.FullName), concreteType);
            }

            var contract = (LogicBlockContractBase)ActivatorUtilities.CreateInstance(_serviceProvider, concreteType, identifier, _actorContext);
            _addContract.Invoke(identifier, contract);

            return contract;
        }

        private void LogAmbiguousContractImplementation(string identifier, Type propertyType, IEnumerable<string?> candidates, Type chosen)
        {
            var loggerFactory = _serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            loggerFactory?.CreateLogger<ContractFactory>()
                         .LogWarning("Contract binding {Identifier} of type {ContractType} has more than one loaded implementation ({Candidates}); binding {Chosen}",
                                     identifier,
                                     propertyType.FullName,
                                     string.Join(", ", candidates),
                                     chosen.FullName);
        }
    }
}