using System;
using System.Globalization;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    // DF-34: a custom value type plus its IChangeThreshold<T>, both declared in THIS test assembly —
    // the shape the runtime must resolve by scanning the property-owning assembly, mirroring the
    // DALE034 analyzer's compilation-visibility model (so a passing compile implies a working runtime).
    public readonly record struct Meters(double Value);

    public sealed class MetersChangeThreshold : IChangeThreshold<Meters>
    {
        public bool Exceeds(in Meters lastEmitted, in Meters candidate, string threshold)
        {
            var min = double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            return Math.Abs(candidate.Value - lastEmitted.Value) >= min;
        }
    }

    [TestClass]
    public class AssemblyScannedChangeThresholdShould
    {
        [TestMethod]
        public void ResolveACustomThresholdDeclaredInTheProbedAssembly()
        {
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Meters), typeof(MetersChangeThreshold).Assembly, out var adapter);

            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds(new Meters(10), new Meters(13), "2"));
            Assert.IsFalse(adapter.Exceeds(new Meters(10), new Meters(11), "2"));
        }

        [TestMethod]
        public void NotResolveWhenNoImplementationIsVisibleInTheProbedAssembly()
        {
            // The probed assembly declares no IChangeThreshold<Guid>; the scan must not fabricate one.
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Guid), typeof(MetersChangeThreshold).Assembly, out var adapter);

            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }
    }
}