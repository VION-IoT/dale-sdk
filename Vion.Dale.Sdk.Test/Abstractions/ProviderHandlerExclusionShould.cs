using System;
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
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Reflection;
using Vion.Dale.Sdk.Test.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     A provider face is development surface a production host must not register: stood up, it claims an
    ///     empty MQTT routing key, which poisons a routing table matched by prefix or substring. The exclusion
    ///     has to be decidable from the handler type alone — there is no handler-to-contract-type link — so the
    ///     marker is the whole mechanism a host gets, and the second test is what holds the two declarations in
    ///     step for the pair this repository ships. The production host that reads the marker is outside this
    ///     repository; what is pinned here is the declaration and the empty registration it stands for.
    /// </summary>
    [TestClass]
    public class ProviderHandlerExclusionShould
    {
        private static readonly Assembly[] IoAssemblies =
        [
            typeof(DigitalOutputHandler).Assembly,
            typeof(AnalogOutputHandler).Assembly,
        ];

        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        [TestProperty("spec", "AC-BIND-015.1")]
        public void LeaveOnlyHardwareHandlersToProductionHostScan()
        {
            // Arrange — the scan a production host runs: the handler convention, minus the marked types.
            var handlers = IoAssemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor));

            // Act
            var registered = handlers.Where(handler => handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>() is null).Select(handler => handler.Name);
            var skipped = handlers.Where(handler => handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>() is not null).Select(handler => handler.Name);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "DigitalInputHandler", "DigitalOutputHandler", "AnalogInputHandler", "AnalogOutputHandler" }, registered.ToList());
            CollectionAssert.AreEquivalent(new[] { "DigitalInputProviderHandler", "DigitalOutputProviderHandler", "AnalogInputProviderHandler", "AnalogOutputProviderHandler" },
                                           skipped.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-015.1")]
        public void MarkHandlerOfEveryDevelopmentOnlyContract()
        {
            // Arrange — the two declarations have no compiler-enforced link, so this is what keeps them in step.
            var simulator = LogicBlockIntrospection.IntrospectLogicBlock(new ProviderContractTestLogicBlock(), _serviceProvider);

            // Act
            var developmentOnly = LogicBlockIntrospection.GetDevelopmentOnlyContracts(simulator);

            // Assert
            Assert.IsNotEmpty(developmentOnly);
            foreach (var contract in developmentOnly)
            {
                var handler = HandlerOf(contract);
                Assert.IsNotNull(handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>(),
                                 $"{handler.Name} services the development-only contract '{contract.Identifier}' and must carry the marker.");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-015.1")]
        public void LeaveHandlerOfOrdinaryContractUnmarked()
        {
            // Arrange
            var block = LogicBlockIntrospection.IntrospectLogicBlock(new ContractTestLogicBlock(), _serviceProvider);

            // Act
            var handlers = block.Contracts.Select(HandlerOf);

            // Assert
            foreach (var handler in handlers)
            {
                Assert.IsNull(handler.GetCustomAttribute<DevelopmentOnlyHandlerAttribute>(),
                              $"{handler.Name} services a hardware contract and must stay registrable in production.");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-015.2")]
        public void RegisterDevelopmentOnlyHandlerWithNoTopicAndEmptyRoutingKey()
        {
            // Arrange
            var context = new LifecycleHarness.RecordingActorContext();
            var handler = new BindProbeProviderHandler();

            // Act
            ((IActorReceiver)handler).HandleMessageAsync(new RegisterMqttHandlerRequest(), context).GetAwaiter().GetResult();

            // Assert
            var registration = context.Sent.Select(sent => sent.Message).OfType<RegisterMqttHandler>().Single();
            Assert.AreEqual(string.Empty, registration.TopicRoutingKey);
            Assert.IsEmpty(registration.TopicGroups.Single().Topics);
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