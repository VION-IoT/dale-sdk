using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Reflection;
using Vion.Dale.Sdk.Test.Introspection;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     A provider handler is development surface a production host must not register: stood up, it claims
    ///     an empty MQTT routing key, which poisons a routing table matched by prefix or substring. The
    ///     exclusion has to be decidable from the handler type alone — there is no handler-to-contract-type
    ///     link — so <c>[DevelopmentOnlyHandler]</c> is the whole mechanism a host gets.
    /// </summary>
    [TestClass]
    public class ProviderHandlerExclusionShould
    {
        private static readonly Assembly[] IoAssemblies =
        {
            typeof(DigitalOutputHandler).Assembly,
            typeof(AnalogOutputHandler).Assembly,
        };

        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        public void LeaveOnlyTheHardwareHandlersToAProductionHostScan()
        {
            // The scan a production host runs: the handler convention, minus the marked types. Nothing else
            // is consulted — no contract type, no attribute on the contract, no instantiation.
            var handlers = IoAssemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor));

            var registered = handlers.Where(handler => handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>() is null).Select(handler => handler.Name).ToList();
            var skipped = handlers.Where(handler => handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>() is not null).Select(handler => handler.Name).ToList();

            CollectionAssert.AreEquivalent(new[] { "DigitalInputHandler", "DigitalOutputHandler", "AnalogInputHandler", "AnalogOutputHandler" }, registered);
            CollectionAssert.AreEquivalent(new[]
                                           {
                                               "DigitalInputProviderHandler", "DigitalOutputProviderHandler", "AnalogInputProviderHandler",
                                               "AnalogOutputProviderHandler",
                                           },
                                           skipped);
        }

        [TestMethod]
        public void MarkTheHandlerOfEveryDevelopmentOnlyContract()
        {
            // The two declarations have no compiler-enforced link, so this is the gate that keeps them in
            // step: a provider face whose handler forgets the marker would be registered in production.
            var simulator = LogicBlockIntrospection.IntrospectLogicBlock(new ProviderContractTestLogicBlock(), _serviceProvider);

            var developmentOnly = LogicBlockIntrospection.GetDevelopmentOnlyContracts(simulator);
            Assert.HasCount(4, developmentOnly);

            foreach (var contract in developmentOnly)
            {
                var handler = HandlerOf(contract);
                Assert.IsNotNull(handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>(),
                                 $"{handler.Name} services the development-only contract '{contract.Identifier}' and must carry [DevelopmentOnlyHandler].");
            }
        }

        [TestMethod]
        public void LeaveTheHandlerOfAnOrdinaryContractUnmarked()
        {
            var block = LogicBlockIntrospection.IntrospectLogicBlock(new ContractTestLogicBlock(), _serviceProvider);

            foreach (var contract in block.Contracts)
            {
                var handler = HandlerOf(contract);
                Assert.IsNull(handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>(),
                              $"{handler.Name} services the hardware contract '{contract.Identifier}' and must stay registrable in production.");
            }
        }

        private static Type HandlerOf(LogicBlockIntrospectionResult.ContractInfo contract)
        {
            var handlerName = (string)contract.Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName];
            var handler = IoAssemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor)).FirstOrDefault(type => type.Name == handlerName);
            Assert.IsNotNull(handler, $"No handler type named '{handlerName}' in the I/O assemblies.");
            return handler;
        }
    }
}