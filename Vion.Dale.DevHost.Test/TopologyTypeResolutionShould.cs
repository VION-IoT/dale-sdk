using System;
using System.Linq;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Regression (VION-69): <c>DevTopologyLoader.ResolveType</c> must not depend on assembly-load timing.
    ///     It used to take two independent <c>AppDomain.CurrentDomain.GetAssemblies()</c> snapshots — one for the
    ///     loaded-type lookup, one for the probe's "already loaded" exclusion set. An assembly loaded by another
    ///     thread between them was missing from the first (so the lookup failed) and present in the second (so
    ///     its file was excluded from the probe), and the topology build then blamed a project reference that was
    ///     there. The seam below is the snapshot source, so the racing order is forced rather than raced for.
    /// </summary>
    [TestClass]
    public class TopologyTypeResolutionShould
    {
        /// <summary>
        ///     The pathological ordering, made deterministic: the first snapshot lacks the declaring assembly,
        ///     the second has it. The two-snapshot code consulted both and resolved nothing; the fix consults
        ///     only the first, finds the assembly absent, and therefore probes its file.
        /// </summary>
        [TestMethod]
        public void ResolveAType_WhoseAssemblyLoadsBetweenSnapshots()
        {
            var declaring = typeof(CounterBlock).Assembly;
            var without = AppDomain.CurrentDomain.GetAssemblies().Where(a => a != declaring).ToArray();
            var with = without.Append(declaring).ToArray();
            var consulted = 0;

            // Whatever the live process has loaded, snapshot 1 is without the declaring assembly and snapshot 2
            // is with it — so no test ordering inside this process can decide what the loader sees.
            var resolved = DevTopologyLoader.ResolveType(typeof(CounterBlock).FullName!, () => consulted++ == 0 ? without : with);

            Assert.AreSame(typeof(CounterBlock), resolved, "A type whose assembly loads between the two snapshots must still resolve — that is the VION-69 race.");
            Assert.AreEqual(1, consulted, "Exactly one snapshot may be consulted per resolution; a second one is the race coming back.");
        }

        /// <summary>
        ///     DF-14 (the reason the probe exists) still holds: a referenced-but-unloaded assembly — absent from
        ///     every snapshot — resolves from the base directory, with no consumer-side ModuleInitializer shim.
        ///     This one passes with and without the VION-69 fix; it is here to pin what the fix must not break.
        /// </summary>
        [TestMethod]
        public void ResolveAType_FromTheProbe_WhenItsAssemblyIsAbsentFromTheSnapshot()
        {
            var declaring = typeof(CounterBlock).Assembly;
            var without = AppDomain.CurrentDomain.GetAssemblies().Where(a => a != declaring).ToArray();

            var resolved = DevTopologyLoader.ResolveType(typeof(CounterBlock).FullName!, () => without);

            Assert.AreSame(typeof(CounterBlock), resolved, "A referenced-but-unloaded assembly must resolve through the base-directory probe (DF-14).");
        }
    }
}