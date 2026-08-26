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
    ///     <para>
    ///         Both rows hide a real, loaded assembly from the injected snapshot, which sends the loader down the
    ///         probe: it walks <c>AppContext.BaseDirectory</c> and loads every not-yet-loaded <c>*.dll</c> until it
    ///         finds the declaring one. That is the production path, but it means these rows pull the output
    ///         directory into the shared test process — keep that in mind if a future fixture assembly lands there.
    ///     </para>
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
            var declaringAssembly = typeof(CounterBlock).Assembly;
            var snapshotWithoutIt = AppDomain.CurrentDomain.GetAssemblies().Where(a => a != declaringAssembly).ToArray();
            var snapshotWithIt = snapshotWithoutIt.Append(declaringAssembly).ToArray();
            var consulted = 0;

            // Whatever the live process has loaded, snapshot 1 is without the declaring assembly and snapshot 2
            // is with it — so no test ordering inside this process can decide what the loader sees.
            var resolved = DevTopologyLoader.ResolveType(typeof(CounterBlock).FullName!, () => consulted++ == 0 ? snapshotWithoutIt : snapshotWithIt);

            Assert.AreSame(typeof(CounterBlock), resolved, "A type whose assembly loads between the two snapshots must still resolve — that is the VION-69 race.");
            Assert.AreEqual(1, consulted, "Exactly one snapshot may be consulted per resolution; a second one is the race coming back.");
        }

        /// <summary>
        ///     The probe (the reason DF-14 needs no consumer-side ModuleInitializer shim) still reaches a type
        ///     whose declaring assembly the snapshot does not list. A genuinely cold assembly cannot be staged in
        ///     a shared MSTest process, so this row hides a loaded one instead — it pins that the probe still runs
        ///     and still resolves, not that a cold load works. Passes with and without the VION-69 fix; it is here
        ///     to catch a fix that breaks the probe.
        /// </summary>
        [TestMethod]
        public void ResolveAType_FromTheProbe_WhenItsAssemblyIsAbsentFromTheSnapshot()
        {
            var declaringAssembly = typeof(CounterBlock).Assembly;
            var snapshotWithoutIt = AppDomain.CurrentDomain.GetAssemblies().Where(a => a != declaringAssembly).ToArray();

            var resolved = DevTopologyLoader.ResolveType(typeof(CounterBlock).FullName!, () => snapshotWithoutIt);

            Assert.AreSame(typeof(CounterBlock), resolved, "An assembly the snapshot does not list must resolve through the base-directory probe.");
        }
    }
}