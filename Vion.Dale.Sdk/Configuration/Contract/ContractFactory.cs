using System;
using Microsoft.Extensions.DependencyInjection;
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
            var concreteType = assemblies.GetConcreteType(propertyType);
            var contract = (LogicBlockContractBase)ActivatorUtilities.CreateInstance(_serviceProvider, concreteType, identifier, _actorContext);
            _addContract.Invoke(identifier, contract);

            return contract;
        }
    }
}
