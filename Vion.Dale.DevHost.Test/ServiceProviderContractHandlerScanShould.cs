using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The convention scan that replaces the hardcoded four-handler list (RFC 0010 increment 2): it
    ///     discovers every <see cref="IServiceProviderHandlerActor" /> the way the runtime does and builds a
    ///     codec for each that declares <c>[ScenarioWire]</c>, skipping the ones that do not (Modbus RTU
    ///     request/response and other out-of-scope handlers).
    /// </summary>
    [TestClass]
    public class ServiceProviderContractHandlerScanShould
    {
        [TestMethod]
        public void Discover_a_codec_for_each_ScenarioWire_handler_and_skip_undeclared()
        {
            var discovered = ServiceProviderContractHandlerScan.Discover([typeof(ProbeInboundHandler).Assembly]);
            var byName = discovered.ToDictionary(d => d.HandlerType.Name, d => d.Codec);

            Assert.IsTrue(byName.ContainsKey(nameof(ProbeInboundHandler)), "An [ScenarioWire(Inbound=...)] handler must be discovered.");
            Assert.IsTrue(byName.ContainsKey(nameof(ProbeOutputHandler)), "An [ScenarioWire(Outbound=...)] handler must be discovered.");
            Assert.IsFalse(byName.ContainsKey(nameof(ProbeUndecoratedHandler)), "A handler without [ScenarioWire] (e.g. Modbus RTU) is out of scope and must be skipped.");

            Assert.IsTrue(byName[nameof(ProbeInboundHandler)].CanDrive);
            Assert.IsTrue(byName[nameof(ProbeOutputHandler)].CanAssert);
        }

        [ScenarioWire(Inbound = typeof(ProbeChanged))]
        private sealed class ProbeInboundHandler : IServiceProviderHandlerActor
        {
            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }

        [ScenarioWire(Outbound = typeof(ProbeSet))]
        private sealed class ProbeOutputHandler : IServiceProviderHandlerActor
        {
            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class ProbeUndecoratedHandler : IServiceProviderHandlerActor
        {
            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }

        private readonly record struct ProbeChanged(bool On);

        private readonly record struct ProbeSet(bool Value);
    }
}