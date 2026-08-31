using System;
using System.Collections.Generic;
using System.Linq;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     One declared pairing before resolution: two (block id, contract identifier) references, exactly what
    ///     <c>DevConfigurationBuilder.PairContracts</c> records and a topology file's <c>contractPairings</c>
    ///     entry names (RFC 0020 §4.2).
    /// </summary>
    internal readonly record struct DeclaredContractPairing(string BlockIdA, string ContractIdentifierA, string BlockIdB, string ContractIdentifierB);

    /// <summary>
    ///     The one place a declared pairing is joined to the endpoints it addresses — shared by the C# builder
    ///     (<c>DevConfigurationBuilder.PairContracts</c>) and the topology-file loader, so both refuse the same
    ///     structure with the same words and only the exception type differs (an API misuse vs. an invalid file).
    ///     <para>
    ///         Structure only: the wire-type identity rule of RFC 0020 §4.3 needs the handler each contract talks
    ///         to, which is known once the blocks are introspected — it runs at host load, in
    ///         <c>ContractPairingTable</c>.
    ///     </para>
    /// </summary>
    internal static class ContractPairingResolution
    {
        /// <summary>
        ///     Resolve each declared pairing against the built configuration's auto-created contract endpoints,
        ///     collecting every problem rather than throwing on the first. Entries that did not resolve are
        ///     omitted from the result, so a caller that reports the errors never carries a half-resolved pairing.
        /// </summary>
        public static List<DevContractPairing> Resolve(DevConfiguration configuration, IEnumerable<DeclaredContractPairing> declared, ICollection<string> errors)
        {
            var resolved = new List<DevContractPairing>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (pairing, index) in declared.Select((p, i) => (p, i)))
            {
                var where = $"contractPairings[{index}]";
                var a = ResolveEndpoint(configuration, pairing.BlockIdA, pairing.ContractIdentifierA, $"{where}.a", errors);
                var b = ResolveEndpoint(configuration, pairing.BlockIdB, pairing.ContractIdentifierB, $"{where}.b", errors);
                if (a is null || b is null)
                {
                    continue;
                }

                // Self-pairing is the dropped host-synthesised confirmation (RFC 0020 §4.5) — an echo back onto
                // the same contract belongs in a simulator block, where it is visible in the topology.
                if (a.LogicBlockId == b.LogicBlockId && a.ContractIdentifier == b.ContractIdentifier)
                {
                    errors.Add($"{where}: both endpoints are '{a.LogicBlockName}.{a.ContractIdentifier}' — a pairing joins two distinct endpoints; " +
                               "an echo back onto the same contract is a simulator block's job, not the host's");
                    continue;
                }

                // The declaration is symmetric, so (a,b) and (b,a) are the same wire. A repeat would install the
                // same forward twice and read as fan-out in the wiring view, which it is not.
                var key = string.CompareOrdinal(Key(a), Key(b)) <= 0 ? $"{Key(a)}|{Key(b)}" : $"{Key(b)}|{Key(a)}";
                if (!seen.Add(key))
                {
                    errors.Add($"{where}: '{a.LogicBlockName}.{a.ContractIdentifier}' and '{b.LogicBlockName}.{b.ContractIdentifier}' are already paired — " +
                               "a pairing is symmetric, so declare it once");
                    continue;
                }

                resolved.Add(new DevContractPairing { A = a, B = b });
            }

            return resolved;
        }

        private static string Key(DevContractPairingEndpoint endpoint)
        {
            return $"{endpoint.LogicBlockId}.{endpoint.ContractIdentifier}";
        }

        private static DevContractPairingEndpoint? ResolveEndpoint(DevConfiguration configuration,
                                                                   string blockId,
                                                                   string contractIdentifier,
                                                                   string where,
                                                                   ICollection<string> errors)
        {
            var block = configuration.LogicBlocks.FirstOrDefault(lb => lb.Id == blockId);
            if (block is null)
            {
                errors.Add($"{where}: '{blockId}' is not a block in this topology");
                return null;
            }

            // The auto-created endpoint for this binding — the same join DevLogicSystemInitializer makes when it
            // builds the contract link map, so a pairing addresses exactly the endpoint a drive would.
            var mapping = block.ContractMappings.FirstOrDefault(cm => cm.ContractIdentifier == contractIdentifier);
            if (mapping is null)
            {
                var available = block.ContractMappings.Count == 0 ? "it binds none" :
                                    $"it binds {string.Join(", ", block.ContractMappings.Select(cm => $"'{cm.ContractIdentifier}'"))}";
                errors.Add($"{where}: block '{block.Name}' has no contract '{contractIdentifier}' — {available}");
                return null;
            }

            return new DevContractPairingEndpoint
                   {
                       LogicBlockId = block.Id,
                       LogicBlockName = block.Name,
                       ContractIdentifier = contractIdentifier,
                       ServiceProviderIdentifier = mapping.ServiceProviderIdentifier,
                       ServiceIdentifier = mapping.ServiceIdentifier,
                       ContractEndpointIdentifier = mapping.ContractEndpointIdentifier,
                   };
        }
    }
}