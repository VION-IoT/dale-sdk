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
            bool resolved = ChangeThresholdRegistry.TryResolve(typeof(double), out IChangeThresholdAdapter adapter);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds(10.0, 12.0, "2"));
            Assert.IsFalse(adapter.Exceeds(10.0, 11.0, "2"));
        }

        [TestMethod]
        public void ResolveBuiltInForTimeSpan()
        {
            bool resolved = ChangeThresholdRegistry.TryResolve(typeof(TimeSpan), out IChangeThresholdAdapter adapter);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "2s"));
        }

        [TestMethod]
        public void ResolveBuiltInsForAllNumericTypes()
        {
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(float), out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(decimal), out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(int), out _));
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(typeof(long), out _));
        }

        [TestMethod]
        public void NotResolveUnregisteredType()
        {
            bool resolved = ChangeThresholdRegistry.TryResolve(typeof(string), out IChangeThresholdAdapter adapter);
            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }

        [TestMethod]
        public void RegisterCustomThresholdAndResolveIt()
        {
            ChangeThresholdRegistry.Register<byte>(new ByteChangeThreshold());

            bool resolved = ChangeThresholdRegistry.TryResolve(typeof(byte), out IChangeThresholdAdapter adapter);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
            Assert.IsTrue(adapter.Exceeds((byte)10, (byte)20, "5"));
            Assert.IsFalse(adapter.Exceeds((byte)10, (byte)12, "5"));
        }

        private sealed class ByteChangeThreshold : IChangeThreshold<byte>
        {
            public bool Exceeds(in byte lastEmitted, in byte candidate, string threshold)
            {
                int minChange = int.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture);
                return Math.Abs(candidate - lastEmitted) >= minChange;
            }
        }
    }
}
