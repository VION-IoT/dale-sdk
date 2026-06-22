using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class ThrottleConfiguredShould
    {
        // Reflection probe: a property carrying an explicitly-configured [ServiceProperty].
        [ServiceProperty(MinInterval = "1s", MinChange = "0.1", Immediate = false)]
        private double ConfiguredProperty { get; set; }

        // Reflection probe: a property carrying a default [ServiceProperty] (no throttle args).
        [ServiceProperty]
        private double DefaultProperty { get; set; }

        // Reflection probe: a property carrying a default [ServiceMeasuringPoint] (no throttle args).
        [ServiceMeasuringPoint]
        private double DefaultMeasuringPoint { get; set; }

        [TestMethod]
        public void SurfaceConfiguredServicePropertyValuesViaIThrottleConfigured()
        {
            var throttle = ThrottleOf<ServicePropertyAttribute>(nameof(ConfiguredProperty));

            Assert.AreEqual("1s", throttle.MinInterval);
            Assert.AreEqual("0.1", throttle.MinChange);
            Assert.IsFalse(throttle.Immediate);
        }

        [TestMethod]
        public void DefaultServicePropertyThrottleValues()
        {
            var throttle = ThrottleOf<ServicePropertyAttribute>(nameof(DefaultProperty));

            Assert.AreEqual("250ms", throttle.MinInterval);
            Assert.IsNull(throttle.MinChange);
            Assert.IsFalse(throttle.Immediate);
        }

        [TestMethod]
        public void DefaultServiceMeasuringPointThrottleValues()
        {
            var throttle = ThrottleOf<ServiceMeasuringPointAttribute>(nameof(DefaultMeasuringPoint));

            Assert.AreEqual("250ms", throttle.MinInterval);
            Assert.IsNull(throttle.MinChange);
            Assert.IsFalse(throttle.Immediate);
        }

        [TestMethod]
        public void ServiceMeasuringPointImplementsIThrottleConfigured()
        {
            Assert.IsInstanceOfType<IThrottleConfigured>(new ServiceMeasuringPointAttribute());
        }

        private static IThrottleConfigured ThrottleOf<TAttribute>(string propertyName)
            where TAttribute : Attribute
        {
            var property = typeof(ThrottleConfiguredShould).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var attribute = property.GetCustomAttribute<TAttribute>()!;
            return (IThrottleConfigured)attribute;
        }
    }
}