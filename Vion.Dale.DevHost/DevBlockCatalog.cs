using System;
using System.Collections.Generic;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     The catalog of every <c>LogicBlockBase</c> type the running DevHost references — every block registered
    ///     by the <c>WithDi&lt;&gt;</c> plugin assemblies, not just the ones in the wired configuration. Computed
    ///     once (lazily, after all <c>WithDi</c> calls) from <c>DevHostBuilder.GetBlockCatalog()</c> and exposed over
    ///     HTTP so a topology-authoring client (RFC 0013 Phase 1) can read the full block menu.
    /// </summary>
    public sealed class DevBlockCatalog
    {
        public IReadOnlyList<Type> Types { get; }

        public DevBlockCatalog(IReadOnlyList<Type> types)
        {
            Types = types;
        }
    }
}