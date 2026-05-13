using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class UiHintsShould
    {
        [TestMethod]
        [DataRow("statusIndicator", nameof(UiHints.StatusIndicator))]
        [DataRow("trigger", nameof(UiHints.Trigger))]
        [DataRow("sparkline", nameof(UiHints.Sparkline))]
        [DataRow("multiline", nameof(UiHints.Multiline))]
        [DataRow("json", nameof(UiHints.Json))]
        [DataRow("slider", nameof(UiHints.Slider))]
        public void ExposeWellKnownConstant(string expected, string member)
        {
            var actual = typeof(UiHints).GetField(member)!.GetRawConstantValue();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void ExposeAllConstantsAsCompileTimeStrings()
        {
            const string statusIndicator = UiHints.StatusIndicator;
            const string trigger = UiHints.Trigger;
            const string sparkline = UiHints.Sparkline;
            const string multiline = UiHints.Multiline;
            const string json = UiHints.Json;
            const string slider = UiHints.Slider;

            Assert.IsGreaterThan(0, statusIndicator.Length);
            Assert.IsGreaterThan(0, trigger.Length);
            Assert.IsGreaterThan(0, sparkline.Length);
            Assert.IsGreaterThan(0, multiline.Length);
            Assert.IsGreaterThan(0, json.Length);
            Assert.IsGreaterThan(0, slider.Length);
        }
    }
}
