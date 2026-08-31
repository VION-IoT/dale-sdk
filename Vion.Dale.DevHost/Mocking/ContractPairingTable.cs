using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    /// <summary>
    ///     The peer one materialised pairing direction forwards to: the stand-in actor's name and the endpoint
    ///     the drive addresses on it.
    /// </summary>
    internal readonly record struct PairedPeer(string HandlerActorName, ServiceProviderContractId Contract);

    /// <summary>
    ///     The pairing lookup a <see cref="ServiceProviderContractHandler" /> consults after capturing a command
    ///     (RFC 0020 §4.1): <c>(handler actor name, endpoint)</c> → the peer stand-in and endpoint the captured
    ///     value is re-driven onto. One entry per materialised DIRECTION, so a pairing that is type-identical
    ///     both ways yields two, and a one-way pairing (a provider face that only drives, like
    ///     <c>IDigitalInputProvider</c>) yields one.
    ///     <para>
    ///         <b>Capture only.</b> The stand-in serves every contract of its handler type, and the drive path
    ///         must never consult this table — a forward that re-entered the drive path would let stand-ins
    ///         originate messages, which RFC 0020 §4.7 forbids because it is what makes a closed loop converge on
    ///         block cadence instead of on stand-in recursion.
    ///     </para>
    /// </summary>
    internal sealed class ContractPairingTable
    {
        /// <summary>The table of an unpaired topology — every capture keeps today's no-forward default.</summary>
        public static readonly ContractPairingTable Empty = new(new Dictionary<(string, ServiceProviderContractId), PairedPeer>());

        private readonly IReadOnlyDictionary<(string HandlerActorName, ServiceProviderContractId Contract), PairedPeer> _peers;

        private ContractPairingTable(IReadOnlyDictionary<(string, ServiceProviderContractId), PairedPeer> peers)
        {
            _peers = peers;
        }

        /// <summary>The peer a capture on this endpoint forwards to, when the direction materialised.</summary>
        public bool TryGetPeer(string handlerActorName, ServiceProviderContractId contract, out PairedPeer peer)
        {
            return _peers.TryGetValue((handlerActorName, contract), out peer);
        }

        /// <summary>
        ///     Resolve every declared pairing to its materialised directions and build the table, refusing the
        ///     whole configuration when a pairing cannot carry anything (RFC 0020 §4.3).
        ///     <para>
        ///         A direction a→b materialises exactly when a declared OUTBOUND type of a's handler is also a
        ///         declared INBOUND type of b's handler — wire-type identity, never shape compatibility, so a
        ///         value is re-delivered and never adapted. Provider faces reuse the consumer face's wire structs,
        ///         which is what makes the canonical pair exact by construction.
        ///     </para>
        /// </summary>
        /// <param name="pairings">The resolved endpoints from the topology file or the C# builder.</param>
        /// <param name="handlerActorNameOf">
        ///     (block id, contract identifier) → the contract's <c>ContractHandlerActorName</c>, or null when the
        ///     contract could not be introspected.
        /// </param>
        /// <param name="codecsByHandler">The <c>[ScenarioWire]</c> codec of every discovered handler, by class name.</param>
        /// <exception cref="InvalidDataException">
        ///     Thrown, listing every problem at once, when a pairing has no materialisable direction or an endpoint
        ///     cannot be joined to a loaded handler.
        /// </exception>
        public static ContractPairingTable Build(IReadOnlyList<DevContractPairing> pairings,
                                                 Func<string, string, string?> handlerActorNameOf,
                                                 IReadOnlyDictionary<string, ScenarioWireCodec> codecsByHandler)
        {
            if (pairings.Count == 0)
            {
                return Empty;
            }

            var peers = new Dictionary<(string, ServiceProviderContractId), PairedPeer>();
            var errors = new List<string>();

            foreach (var (pairing, index) in pairings.Select((p, i) => (p, i)))
            {
                var where = $"contractPairings[{index}]";
                var a = Describe(pairing.A, handlerActorNameOf, codecsByHandler, $"{where}.a", errors);
                var b = Describe(pairing.B, handlerActorNameOf, codecsByHandler, $"{where}.b", errors);
                if (a is null || b is null)
                {
                    continue;
                }

                var aToB = Shared(a.Value.Codec.DeclaredOutbound, b.Value.Codec.DeclaredInbound);
                var bToA = Shared(b.Value.Codec.DeclaredOutbound, a.Value.Codec.DeclaredInbound);

                if (aToB.Count == 0 && bToA.Count == 0)
                {
                    // No "; " inside a single message: the topology subsystem joins its errors with that separator
                    // and the web layer splits them back, so a semicolon here would cut this sentence into two
                    // error rows in the editor.
                    errors.Add($"{where}: '{Name(pairing.A)}' and '{Name(pairing.B)}' have no type-identical direction — " +
                               $"{Wires(pairing.A, a.Value)}, {Wires(pairing.B, b.Value)}. " +
                               "A pairing delivers one side's declared outbound as the other side's identical declared inbound, " +
                               "so pair a contract with its provider face, which reuses the same wire structs.");
                    continue;
                }

                if (aToB.Count > 0)
                {
                    peers[(a.Value.HandlerActorName, Endpoint(pairing.A))] = new PairedPeer(b.Value.HandlerActorName, Endpoint(pairing.B));
                }

                if (bToA.Count > 0)
                {
                    peers[(b.Value.HandlerActorName, Endpoint(pairing.B))] = new PairedPeer(a.Value.HandlerActorName, Endpoint(pairing.A));
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", errors));
            }

            return new ContractPairingTable(peers);
        }

        /// <summary>
        ///     The endpoints a materialised direction FEEDS — the inbound side of every forward. The resolver
        ///     warns when a scenario also drives one of these (legal, last write wins; usually a bench-design
        ///     smell — RFC 0020 §4.6).
        /// </summary>
        public static IEnumerable<(DevContractPairingEndpoint Source, DevContractPairingEndpoint Fed)> FedDirections(IReadOnlyList<DevContractPairing> pairings,
                                                                                                                     Func<string, string, string?> handlerActorNameOf,
                                                                                                                     IReadOnlyDictionary<string, ScenarioWireCodec> codecsByHandler)
        {
            foreach (var pairing in pairings)
            {
                var a = Describe(pairing.A, handlerActorNameOf, codecsByHandler, string.Empty, new List<string>());
                var b = Describe(pairing.B, handlerActorNameOf, codecsByHandler, string.Empty, new List<string>());
                if (a is null || b is null)
                {
                    continue;
                }

                if (Shared(a.Value.Codec.DeclaredOutbound, b.Value.Codec.DeclaredInbound).Count > 0)
                {
                    yield return (pairing.A, pairing.B);
                }

                if (Shared(b.Value.Codec.DeclaredOutbound, a.Value.Codec.DeclaredInbound).Count > 0)
                {
                    yield return (pairing.B, pairing.A);
                }
            }
        }

        private static ServiceProviderContractId Endpoint(DevContractPairingEndpoint endpoint)
        {
            return new ServiceProviderContractId(endpoint.ServiceProviderIdentifier, endpoint.ServiceIdentifier, endpoint.ContractEndpointIdentifier);
        }

        private static string Name(DevContractPairingEndpoint endpoint)
        {
            return $"{endpoint.LogicBlockName}.{endpoint.ContractIdentifier}";
        }

        // Both declared directions of one endpoint's handler, named — the message a refusal is judged by, so the
        // author sees WHICH types were compared rather than that "they don't match".
        private static string Wires(DevContractPairingEndpoint endpoint, (string HandlerActorName, ScenarioWireCodec Codec) resolved)
        {
            string List(IReadOnlyCollection<Type> types)
            {
                return types.Count == 0 ? "none" : string.Join("/", types.Select(t => t.Name));
            }

            return $"'{Name(endpoint)}' ({resolved.HandlerActorName}) declares outbound {List(resolved.Codec.DeclaredOutbound)} and inbound {List(resolved.Codec.DeclaredInbound)}";
        }

        private static IReadOnlyCollection<Type> Shared(IReadOnlyCollection<Type> outbound, IReadOnlyCollection<Type> inbound)
        {
            return outbound.Where(inbound.Contains).ToList();
        }

        private static (string HandlerActorName, ScenarioWireCodec Codec)? Describe(DevContractPairingEndpoint endpoint,
                                                                                    Func<string, string, string?> handlerActorNameOf,
                                                                                    IReadOnlyDictionary<string, ScenarioWireCodec> codecsByHandler,
                                                                                    string where,
                                                                                    ICollection<string> errors)
        {
            var handlerName = handlerActorNameOf(endpoint.LogicBlockId, endpoint.ContractIdentifier);
            if (handlerName is null)
            {
                errors.Add($"{where}: block '{endpoint.LogicBlockName}' has no contract '{endpoint.ContractIdentifier}'");
                return null;
            }

            if (!codecsByHandler.TryGetValue(handlerName, out var codec))
            {
                // Two different faults, two different fixes — say which one it is rather than "no wire (or did
                // not load)": a loaded handler needs a [ScenarioWire] added to it, an absent one needs its
                // library referenced by this host.
                errors.Add(ServiceProviderContractHandlerScan.IsHandlerTypeLoaded(handlerName) ?
                               $"{where}: '{Name(endpoint)}' is serviced by '{handlerName}', which declares no [ScenarioWire] — " +
                               "a pairing carries the wire structs a handler declares, so an undeclared handler has no direction to offer" :
                               $"{where}: '{Name(endpoint)}' is serviced by '{handlerName}', which is not loaded — no service-provider handler of that name is " +
                               "among this host's assemblies, so reference the library that declares it");
                return null;
            }

            return (handlerName, codec);
        }
    }
}