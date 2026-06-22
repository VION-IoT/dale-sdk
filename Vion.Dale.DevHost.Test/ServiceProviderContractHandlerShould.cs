using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The generic DevHost stand-in (RFC 0010): one handler, discovered by convention scan, replaces the
    ///     four hardcoded <c>MockHal*Handler</c> classes. It drives any <c>[ScenarioWire]</c> value contract
    ///     into its consuming block via the codec — the DF-27 unblock — and captures outbound commands, raising
    ///     the one generic <see cref="DevHostEvents.ServiceProviderContractChanged" /> event for the live UI and
    ///     the <c>serviceProviderExpect</c> read source. No type-specific events, no output echo.
    /// </summary>
    [TestClass]
    public class ServiceProviderContractHandlerShould
    {
        private static readonly ServiceProviderContractId Sp = new("sp", "svc", "c");

        private static readonly LogicBlockContractId Lb = new(new LogicBlockId("lb1"), "EnableInput");

        [TestMethod]
        public void Forward_a_driven_single_field_input_to_the_consuming_block()
        {
            var handler = NewHandler(typeof(ScalarInputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("true")), context);

            var sent = context.Sent.Single();
            Assert.AreSame(consumer, sent.Target);
            Assert.IsInstanceOfType<ContractMessage<ScalarChanged>>(sent.Message);
            var message = (ContractMessage<ScalarChanged>)sent.Message;
            Assert.IsTrue(message.Data.On);
            Assert.AreEqual(Lb, message.LogicBlockContractId);
        }

        [TestMethod]
        public void Forward_a_driven_multi_field_custom_contract_to_the_consuming_block()
        {
            // The DF-27 unblock: a third-party value contract (PPC-shaped multi-field struct, enum-by-name)
            // is driven through the SAME generic handler with no per-contract code.
            var handler = NewHandler(typeof(DemandInputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("""{ "valid": true, "scope": "PerPhase", "activePowerW": 1500 }""")), context);

            var demand = ((ContractMessage<DemandChanged>)context.Sent.Single().Message).Data;
            Assert.IsTrue(demand.Valid);
            Assert.AreEqual(DemandScope.PerPhase, demand.Scope);
            Assert.AreEqual(1500d, demand.ActivePowerW);
        }

        [TestMethod]
        public void Forward_a_driven_input_to_every_block_mapped_to_the_contract()
        {
            var handler = NewHandler(typeof(ScalarInputHandlerStub));
            var context = new RecordingActorContext();
            var first = new FakeActorReference();
            var second = new FakeActorReference();
            var firstContract = new LogicBlockContractId(new LogicBlockId("lb1"), "EnableInput");
            var secondContract = new LogicBlockContractId(new LogicBlockId("lb2"), "EnableInput");

            handler.HandleMessageAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                        {
                                                                            [Sp] = new()
                                                                                   {
                                                                                       [firstContract] = first,
                                                                                       [secondContract] = second,
                                                                                   },
                                                                        }),
                                       context);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("true")), context);

            CollectionAssert.AreEquivalent(new IActorReference[] { first, second }, context.Sent.Select(s => s.Target).ToList());
        }

        [TestMethod]
        public void Capture_an_output_command_raising_the_generic_event_without_echoing()
        {
            // An outbound command a block Set raises the one generic ServiceProviderContractChanged event (the
            // SPA read-out + the serviceProviderExpect read source). The DevHost does NOT synthesize a typed
            // output-confirmation back to the block — the real upstream confirms over MQTT, not the simulation.
            var events = new DevHostEvents();
            var handler = NewHandler(typeof(ScalarOutputHandlerStub), events);
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            ServiceProviderContractChangedEventArgs? raised = null;
            events.ServiceProviderContractChanged += (_, e) => raised = e;

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new ContractMessage<SetScalar>(Lb, new SetScalar(true)), context);

            Assert.IsNotNull(raised, "An outbound command must raise the generic ServiceProviderContractChanged event.");
            Assert.AreEqual(Sp.ServiceProviderIdentifier, raised!.ServiceProviderIdentifier);
            Assert.AreEqual(Sp.ContractIdentifier, raised.ContractIdentifier);
            Assert.IsTrue(raised.Value.GetBoolean());

            Assert.IsEmpty(context.Sent, "Capture must not echo a confirmation back to the block.");
        }

        [TestMethod]
        public void Ignore_a_drive_on_an_output_only_contract()
        {
            // serviceProviderSet on an output is a validation error at the scenario layer; the handler must
            // never fabricate an inbound from an output-only codec.
            var handler = NewHandler(typeof(ScalarOutputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("true")), context);

            Assert.IsEmpty(context.Sent);
        }

        private static ServiceProviderContractHandler NewHandler(Type wireHandlerType, DevHostEvents? events = null)
        {
            var codec = ScenarioWireCodec.ForHandler(wireHandlerType)!;
            return new ServiceProviderContractHandler(NullLogger.Instance, events ?? new DevHostEvents(), codec, new Control.ServiceProviderOutputCache());
        }

        private static void Link(ServiceProviderContractHandler handler, IActorContext context, IActorReference consumer)
        {
            handler.HandleMessageAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                        {
                                                                            [Sp] = new() { [Lb] = consumer },
                                                                        }),
                                       context);
        }

        private static JsonElement Json(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        [ScenarioWire(Inbound = typeof(ScalarChanged))]
        private sealed class ScalarInputHandlerStub
        {
        }

        [ScenarioWire(Inbound = typeof(DemandChanged))]
        private sealed class DemandInputHandlerStub
        {
        }

        [ScenarioWire(Outbound = typeof(SetScalar))]
        private sealed class ScalarOutputHandlerStub
        {
        }

        private readonly record struct ScalarChanged(bool On);

        private readonly record struct DemandChanged(bool Valid, DemandScope Scope, double ActivePowerW);

        private readonly record struct SetScalar(bool Value);

        private enum DemandScope
        {
            Total,

            PerPhase,
        }

        private sealed class FakeActorReference : IActorReference
        {
        }

        private sealed class RecordingActorContext : IActorContext
        {
            public List<(IActorReference Target, object Message)> Sent { get; } = [];

            public IReadOnlyDictionary<string, string>? Headers
            {
                get => null;
            }

            public void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null)
            {
                Sent.Add((target, message));
            }

            public void SendToSelf(object message)
            {
            }

            public void SendToSelfAfter(object message, TimeSpan delay)
            {
            }

            public void RespondToSender(object message)
            {
            }

            public IActorReference LookupByName(string name)
            {
                throw new NotSupportedException();
            }
        }
    }
}