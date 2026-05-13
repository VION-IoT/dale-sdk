using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class PropertyGroupShould
    {
        [TestMethod]
        [DataRow("", nameof(PropertyGroup.None))]
        [DataRow("identity", nameof(PropertyGroup.Identity))]
        [DataRow("status", nameof(PropertyGroup.Status))]
        [DataRow("configuration", nameof(PropertyGroup.Configuration))]
        [DataRow("metric", nameof(PropertyGroup.Metric))]
        [DataRow("diagnostics", nameof(PropertyGroup.Diagnostics))]
        [DataRow("alarm", nameof(PropertyGroup.Alarm))]
        public void ExposeWellKnownConstant(string expected, string member)
        {
            var actual = typeof(PropertyGroup).GetField(member)!.GetRawConstantValue();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ExposeAllConstantsAsCompileTimeStrings()
        {
            // Compile-time const usage — defeats accidental conversion to readonly.
            const string none = PropertyGroup.None;
            const string identity = PropertyGroup.Identity;
            const string status = PropertyGroup.Status;
            const string configuration = PropertyGroup.Configuration;
            const string metric = PropertyGroup.Metric;
            const string diagnostics = PropertyGroup.Diagnostics;
            const string alarm = PropertyGroup.Alarm;

            Assert.AreEqual(0, none.Length);
            Assert.IsGreaterThan(0, identity.Length);
            Assert.IsGreaterThan(0, status.Length);
            Assert.IsGreaterThan(0, configuration.Length);
            Assert.IsGreaterThan(0, metric.Length);
            Assert.IsGreaterThan(0, diagnostics.Length);
            Assert.IsGreaterThan(0, alarm.Length);
        }
    }
}
