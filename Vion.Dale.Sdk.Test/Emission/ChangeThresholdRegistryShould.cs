using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class ChangeThresholdRegistryShould
    {
        [TestMethod]
        public void ResolveBuiltInForDouble()
        {
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(double), null, out var adapter);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds(10.0, 12.0, "2"));
            Assert.IsFalse(adapter.Exceeds(10.0, 11.0, "2"));
        }

        [TestMethod]
        public void ResolveBuiltInForTimeSpan()
        {
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(TimeSpan), null, out var adapter);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "2s"));
        }

        [TestMethod]
        public void ResolveBuiltInsForAllNumericTypes()
        {
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(float), null, out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(decimal), null, out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(int), null, out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(long), null, out _));
        }

        [TestMethod]
        public void NotResolveUnregisteredType()
        {
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(string), null, out var adapter);
            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }

    }
}