using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class PresentationAttributeShould
    {
        public class Subject
        {
            [Presentation(
                DisplayName = "Wirkleistung",
                Group = PropertyGroup.Status,
                Order = -1,
                Importance = Importance.Primary,
                StatusIndicator = false,
                Decimals = 1,
                UiHint = UiHints.Sparkline)]
            public double WithAllFields { get; set; }

            [Presentation]
            public double Defaulted { get; set; }
        }

        [TestMethod]
        public void CarryAllFields()
        {
            var prop = typeof(Subject).GetProperty(nameof(Subject.WithAllFields))!;
            var p = prop.GetCustomAttribute<PresentationAttribute>()!;
            Assert.AreEqual("Wirkleistung", p.DisplayName);
            Assert.AreEqual(PropertyGroup.Status, p.Group);
            Assert.AreEqual(-1, p.Order);
            Assert.AreEqual(Importance.Primary, p.Importance);
            Assert.IsFalse(p.StatusIndicator);
            Assert.AreEqual(1, p.Decimals);
            Assert.AreEqual(UiHints.Sparkline, p.UiHint);
        }

        [TestMethod]
        public void DefaultDisplayNameToNull()
        {
            var attr = new PresentationAttribute();
            Assert.IsNull(attr.DisplayName);
        }

        [TestMethod]
        public void DefaultGroupToNull()
        {
            var attr = new PresentationAttribute();
            Assert.IsNull(attr.Group);
        }

        [TestMethod]
        public void DefaultOrderToSentinel()
        {
            // int.MinValue sentinel is required because attribute parameters can't be nullable.
            // PropertyMetadataBuilder converts the sentinel to null when building the codec-side Presentation.
            var attr = new PresentationAttribute();
            Assert.AreEqual(int.MinValue, attr.Order);
        }

        [TestMethod]
        public void DefaultImportanceToNormal()
        {
            var attr = new PresentationAttribute();
            Assert.AreEqual(Importance.Normal, attr.Importance);
        }

        [TestMethod]
        public void DefaultStatusIndicatorToFalse()
        {
            var attr = new PresentationAttribute();
            Assert.IsFalse(attr.StatusIndicator);
        }

        [TestMethod]
        public void DefaultDecimalsToSentinel()
        {
            var attr = new PresentationAttribute();
            Assert.AreEqual(int.MinValue, attr.Decimals);
        }

        [TestMethod]
        public void DefaultUiHintToNull()
        {
            var attr = new PresentationAttribute();
            Assert.IsNull(attr.UiHint);
        }

        [TestMethod]
        public void DefaultedPropertyCarriesDefaults()
        {
            var prop = typeof(Subject).GetProperty(nameof(Subject.Defaulted))!;
            var p = prop.GetCustomAttribute<PresentationAttribute>()!;
            Assert.IsNull(p.DisplayName);
            Assert.IsNull(p.Group);
            Assert.AreEqual(Importance.Normal, p.Importance);
            Assert.IsNull(p.Format);
        }

        [TestMethod]
        public void CarryFormatField()
        {
            var attr = new PresentationAttribute { Format = "LLLL" };
            Assert.AreEqual("LLLL", attr.Format);
        }

        [TestMethod]
        public void CarryFormatSentinel()
        {
            var attr = new PresentationAttribute { Format = Formats.Relative };
            Assert.AreEqual("relative", attr.Format);
        }

        [TestMethod]
        public void DefaultFormatToNull()
        {
            var attr = new PresentationAttribute();
            Assert.IsNull(attr.Format);
        }
    }
}
