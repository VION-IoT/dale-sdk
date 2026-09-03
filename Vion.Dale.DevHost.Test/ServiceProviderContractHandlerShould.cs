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
    ///     the <c>serviceProviderExpect</c> read source. No type-specific events, no output echo — and, when the
    ///     topology paired the endpoint, one forward of the captured value onto the peer stand-in (RFC 0020).
    /// </summary>
    [TestClass]
    public class ServiceProviderContractHandlerShould
    {
        private static readonly ServiceProviderContractId Sp = new("sp", "svc", "c");

        private static readonly ServiceProviderContractId Peer = new("sp_peer", "svc_peer", "OutputChannel");

        private static readonly LogicBlockContractId Lb = new(new LogicBlockId("lb1"), "EnableInput");

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.2")]
        public void ForwardDrivenSingleFieldInputToConsumingBlock()
        {
            // Arrange / Act
            var handler = NewHandler(typeof(ScalarInputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("true")), context);

            var sent = context.Sent.Single();
            // Assert
            Assert.AreSame(consumer, sent.Target);
            Assert.IsInstanceOfType<ContractMessage<ScalarChanged>>(sent.Message);
            var message = (ContractMessage<ScalarChanged>)sent.Message;
            Assert.IsTrue(message.Data.On);
            Assert.AreEqual(Lb, message.LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.4")]
        public void ForwardDrivenMultiFieldCustomContractToConsumingBlock()
        {
            // Arrange / Act
            // The DF-27 unblock: a third-party value contract (PPC-shaped multi-field struct, enum-by-name)
            // is driven through the SAME generic handler with no per-contract code.
            var handler = NewHandler(typeof(DemandInputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("""{ "valid": true, "scope": "PerPhase", "activePowerW": 1500 }""")), context);

            var demand = ((ContractMessage<DemandChanged>)context.Sent.Single().Message).Data;
            // Assert
            Assert.IsTrue(demand.Valid);
            Assert.AreEqual(DemandScope.PerPhase, demand.Scope);
            Assert.AreEqual(1500d, demand.ActivePowerW);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.13")]
        public void ForwardDrivenInputToEveryBlockMappedToContract()
        {
            // Arrange / Act
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

            // Assert
            CollectionAssert.AreEquivalent(new IActorReference[] { first, second }, context.Sent.Select(s => s.Target).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.11")]
        public void CaptureOutputCommandRaisingGenericEventAndSendNothingWhenEndpointUnpaired()
        {
            // Arrange / Act
            // The DEFAULT, pinned (RFC 0020 §4.1): an outbound command a block Set raises the one generic
            // ServiceProviderContractChanged event (the SPA read-out + the serviceProviderExpect read source) and
            // goes nowhere else. The DevHost does NOT synthesize a typed output-confirmation back to the block —
            // the real upstream confirms over MQTT, not the simulation — and with no pairing declared there is no
            // peer to forward to either.
            var events = new DevHostEvents();
            var handler = NewHandler(typeof(ScalarOutputHandlerStub), events);
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            ServiceProviderContractChangedEventArgs? raised = null;
            events.ServiceProviderContractChanged += (_, e) => raised = e;

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new ContractMessage<SetScalar>(Lb, new SetScalar(true)), context);

            // Assert
            Assert.IsNotNull(raised, "An outbound command must raise the generic ServiceProviderContractChanged event.");
            Assert.AreEqual(Sp.ServiceProviderIdentifier, raised!.ServiceProviderIdentifier);
            Assert.AreEqual(Sp.ContractIdentifier, raised.ContractIdentifier);
            Assert.IsTrue(raised.Value.GetBoolean());

            Assert.IsEmpty(context.Sent, "Capture must neither echo a confirmation back to the block nor forward when nothing is paired.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.11")]
        public void ForwardCapturedCommandToPairedPeerStandIn()
        {
            // Arrange / Act
            // The pairing primitive: with the endpoint paired to a provider face whose declared inbound is the
            // SAME wire struct, the captured value is re-driven onto the PEER stand-in as the ordinary drive
            // message — no new message type, no transformation, and nothing sent back to the writing block.
            var handler = NewHandler(typeof(ConfirmedOutputHandlerStub), handlerActorName: nameof(ConfirmedOutputHandlerStub), pairings: OutputPairedToItsProviderFace());
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new ContractMessage<SetScalar>(Lb, new SetScalar(true)), context);

            var sent = context.Sent.Single();
            // Assert
            Assert.AreSame(context.LookupByName(nameof(ScalarProviderHandlerStub)), sent.Target, "The forward addresses the PEER stand-in, not the writing block.");
            var drive = (MockSetServiceProviderInputMessage)sent.Message;
            Assert.AreEqual(Peer, drive.Contract);
            Assert.IsTrue(drive.Value.GetBoolean());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.9")]
        public void RecordPairedCommandInOutputCacheBeforeForwardingIt()
        {
            // Arrange / Act
            // serviceProviderExpect must still read what a PAIRED output wrote — the cache write happens before
            // the forward, so pairing never costs an assertion.
            var cache = new Control.ServiceProviderOutputCache();
            var handler = NewHandler(typeof(ConfirmedOutputHandlerStub),
                                     handlerActorName: nameof(ConfirmedOutputHandlerStub),
                                     pairings: OutputPairedToItsProviderFace(),
                                     outputCache: cache);
            var context = new RecordingActorContext();

            Link(handler, context, new FakeActorReference());
            handler.HandleMessageAsync(new ContractMessage<SetScalar>(Lb, new SetScalar(true)), context);

            // Assert
            Assert.IsTrue(cache.TryGet(Sp, out var recorded));
            Assert.IsTrue(recorded.GetBoolean());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.10")]
        public void NeverConsultPairingTableOnDrivePath()
        {
            // Arrange / Act
            // RFC 0020 §4.7: only Capture forwards. A drive that also forwarded would let stand-ins originate
            // messages, and a closed loop would converge on stand-in recursion rather than on block cadence.
            var handler = NewHandler(typeof(ScalarProviderHandlerStub), handlerActorName: nameof(ScalarProviderHandlerStub), pairings: OutputPairedToItsProviderFace());
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            handler.HandleMessageAsync(new LinkLogicBlockContractActors(new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>
                                                                        {
                                                                            [Peer] = new() { [Lb] = consumer },
                                                                        }),
                                       context);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Peer, Json("true")), context);

            var sent = context.Sent.Single();
            // Assert
            Assert.AreSame(consumer, sent.Target, "A drive reaches the mapped block and nothing else.");
            Assert.IsInstanceOfType<ContractMessage<SetScalar>>(sent.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.13")]
        public void IgnoreDriveOnOutputOnlyContract()
        {
            // Arrange / Act
            // serviceProviderSet on an output is a validation error at the scenario layer; the handler must
            // never fabricate an inbound from an output-only codec.
            var handler = NewHandler(typeof(ScalarOutputHandlerStub));
            var context = new RecordingActorContext();
            var consumer = new FakeActorReference();

            Link(handler, context, consumer);
            handler.HandleMessageAsync(new MockSetServiceProviderInputMessage(Sp, Json("true")), context);

            // Assert
            Assert.IsEmpty(context.Sent);
        }

        private static ServiceProviderContractHandler NewHandler(Type wireHandlerType,
                                                                 DevHostEvents? events = null,
                                                                 string handlerActorName = "StandIn",
                                                                 ContractPairingTable? pairings = null,
                                                                 Control.ServiceProviderOutputCache? outputCache = null)
        {
            var codec = ScenarioWireCodec.ForHandler(wireHandlerType)!;
            return new ServiceProviderContractHandler(NullLogger.Instance,
                                                      events ?? new DevHostEvents(),
                                                      codec,
                                                      outputCache ?? new Control.ServiceProviderOutputCache(),
                                                      handlerActorName,
                                                      pairings ?? ContractPairingTable.Empty);
        }

        // The canonical pair: a confirmed output (SetScalar out / ScalarChanged in) wired to its provider face
        // (the exact inverse). Type-identical in BOTH directions, so both forwards materialise.
        private static ContractPairingTable OutputPairedToItsProviderFace()
        {
            var pairing = new DevContractPairing
                          {
                              A = new DevContractPairingEndpoint
                                  {
                                      LogicBlockId = "lb1",
                                      LogicBlockName = "IoBlock",
                                      ContractIdentifier = "ActiveOutput",
                                      ServiceProviderIdentifier = Sp.ServiceProviderIdentifier,
                                      ServiceIdentifier = Sp.ServiceIdentifier,
                                      ContractEndpointIdentifier = Sp.ContractIdentifier,
                                  },
                              B = new DevContractPairingEndpoint
                                  {
                                      LogicBlockId = "lb2",
                                      LogicBlockName = "IdealIo",
                                      ContractIdentifier = "OutputChannel",
                                      ServiceProviderIdentifier = Peer.ServiceProviderIdentifier,
                                      ServiceIdentifier = Peer.ServiceIdentifier,
                                      ContractEndpointIdentifier = Peer.ContractIdentifier,
                                  },
                          };

            return ContractPairingTable.Build([pairing],
                                              (blockId, _) => blockId == "lb1" ? nameof(ConfirmedOutputHandlerStub) : nameof(ScalarProviderHandlerStub),
                                              new Dictionary<string, ScenarioWireCodec>
                                              {
                                                  [nameof(ConfirmedOutputHandlerStub)] =
                                                      ScenarioWireCodec.ForHandler(typeof(ConfirmedOutputHandlerStub))!,
                                                  [nameof(ScalarProviderHandlerStub)] =
                                                      ScenarioWireCodec.ForHandler(typeof(ScalarProviderHandlerStub))!,
                                              });
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

        // The shape of DigitalOutputHandler since VION-131: an output whose provider confirms back.
        [ScenarioWire(Inbound = typeof(ScalarChanged), Outbound = typeof(SetScalar))]
        private sealed class ConfirmedOutputHandlerStub
        {
        }

        // Its provider face, exactly inverted — the identity that makes a pairing type-identical both ways.
        [ScenarioWire(Inbound = typeof(SetScalar), Outbound = typeof(ScalarChanged))]
        private sealed class ScalarProviderHandlerStub
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
            private readonly Dictionary<string, IActorReference> _byName = new(StringComparer.Ordinal);

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

            // Stable per name, so a test can assert WHICH stand-in a forward addressed.
            public IActorReference LookupByName(string name)
            {
                if (!_byName.TryGetValue(name, out var reference))
                {
                    _byName[name] = reference = new FakeActorReference();
                }

                return reference;
            }
        }
    }
}