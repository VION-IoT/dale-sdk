using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     The three emission knobs as an author leaves them on either attribute: what a declaration says,
    ///     and what an omitted knob means. Both attributes declare the same set with the same defaults, so
    ///     the gate reads either through one interface without knowing which stream it is building.
    /// </summary>
    [TestClass]
    public class ThrottleConfiguredShould
    {
        [ServiceProperty]
        private double BareProperty { get; set; }

        [ServiceMeasuringPoint]
        private double BareMeasuringPoint { get; set; }

        [TestMethod]
        [DataRow(typeof(ServicePropertyAttribute), nameof(ConfiguredProperty), DisplayName = "service property")]
        [DataRow(typeof(ServiceMeasuringPointAttribute), nameof(ConfiguredMeasuringPoint), DisplayName = "measuring point")]
        public void SurfaceEveryDeclaredKnob(Type attributeType, string propertyName)
        {
            // Arrange / Act
            var knobs = KnobsOf(attributeType, propertyName);

            // Assert
            Assert.AreEqual("1s", knobs.MinInterval);
            Assert.AreEqual("0.1", knobs.MinChange);
            Assert.IsTrue(knobs.Immediate);
        }

        [TestMethod]
        [DataRow(typeof(ServicePropertyAttribute), nameof(BareProperty), DisplayName = "service property")]
        [DataRow(typeof(ServiceMeasuringPointAttribute), nameof(BareMeasuringPoint), DisplayName = "measuring point")]
        public void SurfaceSameDefaultsForBothAttributes(Type attributeType, string propertyName)
        {
            // Arrange / Act
            var knobs = KnobsOf(attributeType, propertyName);

            // Assert — an omitted interval is the 250 ms default, an omitted deadband is no deadband.
            Assert.AreEqual("250ms", knobs.MinInterval);
            Assert.IsNull(knobs.MinChange);
            Assert.IsFalse(knobs.Immediate);
        }

        private static IThrottleConfigured KnobsOf(Type attributeType, string propertyName)
        {
            var property = typeof(ThrottleConfiguredShould).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (IThrottleConfigured)property.GetCustomAttribute(attributeType)!;
        }

        // Deliberately illegal: DALE038 warns that Immediate makes the other two knobs inert, which is the
        // point — these probes assert that every knob an author writes is surfaced, including the ignored
        // ones. Suppressed so the SDK's own build stays warning-free.
#pragma warning disable DALE038
        [ServiceProperty(MinInterval = "1s", MinChange = "0.1", Immediate = true)]
        private double ConfiguredProperty { get; set; }

        [ServiceMeasuringPoint(MinInterval = "1s", MinChange = "0.1", Immediate = true)]
        private double ConfiguredMeasuringPoint { get; set; }
#pragma warning restore DALE038
    }
}