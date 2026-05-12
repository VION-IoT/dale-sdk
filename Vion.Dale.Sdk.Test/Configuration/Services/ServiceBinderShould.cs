using System;
using System.Collections.Immutable;
using Vion.Dale.Sdk.Test.Introspection;

namespace Vion.Dale.Sdk.Test.Configuration.Services
{
    // -----------------------------------------------------------------------
    // Regression-baseline tests for ServiceBinder.SetPropertyValue /
    // GetPropertyValue / GetMeasuringPointValue across every type kind.
    //
    // These tests document behaviour POST-LT9 (§3.13): the ad-hoc int→enum
    // conversion at ServiceBinder.cs was removed. The codec now produces typed
    // enums. The CLR's compiled-expression setter can still accept a boxed int
    // for an enum property when the underlying types match, so the legacy path
    // technically continues to round-trip — but is no longer an intentional
    // feature of the binder. The canonical path is to pass typed enum values.
    // -----------------------------------------------------------------------
    [TestClass]
    public class ServiceBinderShould
    {
        private const string ServiceId = "RichDevice";

        // ===================================================================
        // Primitives — round-trip
        // ===================================================================

        [TestMethod]
        public void RoundTrip_Bool()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "BoolValue", true);
            Assert.IsTrue((bool)binder.GetPropertyValue(ServiceId, "BoolValue")!);

            binder.SetPropertyValue(ServiceId, "BoolValue", false);
            Assert.IsFalse((bool)binder.GetPropertyValue(ServiceId, "BoolValue")!);
        }

        [TestMethod]
        public void RoundTrip_Byte()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "ByteValue", (byte)42);
            Assert.AreEqual((byte)42, binder.GetPropertyValue(ServiceId, "ByteValue"));
        }

        [TestMethod]
        public void RoundTrip_UShort()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "UShortValue", (ushort)400);
            Assert.AreEqual((ushort)400, binder.GetPropertyValue(ServiceId, "UShortValue"));
        }

        [TestMethod]
        public void RoundTrip_UInt()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "UIntValue", 123u);
            Assert.AreEqual(123u, binder.GetPropertyValue(ServiceId, "UIntValue"));
        }

        [TestMethod]
        public void RoundTrip_Long()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "LongValue", 9_000_000_000L);
            Assert.AreEqual(9_000_000_000L, binder.GetPropertyValue(ServiceId, "LongValue"));
        }

        [TestMethod]
        public void RoundTrip_Double()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "VoltageSetpoint", 3.14);
            Assert.AreEqual(3.14, binder.GetPropertyValue(ServiceId, "VoltageSetpoint"));
        }

        // ===================================================================
        // Nullable primitives — round-trip
        // ===================================================================

        [TestMethod]
        public void RoundTrip_NullableDouble_NullThenValue()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "Target", null);
            Assert.IsNull(binder.GetPropertyValue(ServiceId, "Target"));

            binder.SetPropertyValue(ServiceId, "Target", 1.5);
            Assert.AreEqual(1.5, binder.GetPropertyValue(ServiceId, "Target"));
        }

        [TestMethod]
        public void RoundTrip_NullableInt_NullThenZeroThenValue()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "OptionalCount", null);
            Assert.IsNull(binder.GetPropertyValue(ServiceId, "OptionalCount"));

            binder.SetPropertyValue(ServiceId, "OptionalCount", 0);
            Assert.AreEqual(0, binder.GetPropertyValue(ServiceId, "OptionalCount"));

            binder.SetPropertyValue(ServiceId, "OptionalCount", 42);
            Assert.AreEqual(42, binder.GetPropertyValue(ServiceId, "OptionalCount"));
        }

        // ===================================================================
        // Strings
        // ===================================================================

        [TestMethod]
        public void RoundTrip_NonNullString()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "NonNullName", "foo");
            Assert.AreEqual("foo", binder.GetPropertyValue(ServiceId, "NonNullName"));
        }

        [TestMethod]
        public void RoundTrip_NullableString_NullThenValue()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "OptionalErrorMessage", null);
            Assert.IsNull(binder.GetPropertyValue(ServiceId, "OptionalErrorMessage"));

            binder.SetPropertyValue(ServiceId, "OptionalErrorMessage", "bar");
            Assert.AreEqual("bar", binder.GetPropertyValue(ServiceId, "OptionalErrorMessage"));
        }

        // ===================================================================
        // Enums — regression-sensitive
        // ===================================================================

        [TestMethod]
        public void RoundTrip_Enum_TypedValue()
        {
            // Pass the enum member directly (this is the post-LT9 canonical path).
            var (binder, block) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "PreferredMode", OperatingMode.Manual);
            Assert.AreEqual(OperatingMode.Manual, binder.GetPropertyValue(ServiceId, "PreferredMode"));

            // Verify the backing field on the block itself for extra confidence.
            Assert.AreEqual(OperatingMode.Manual, block.PreferredMode);
        }

        [TestMethod]
        public void RoundTrip_Enum_AutoValue()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "PreferredMode", OperatingMode.Auto);
            Assert.AreEqual(OperatingMode.Auto, binder.GetPropertyValue(ServiceId, "PreferredMode"));
        }

        /// <summary>
        ///     Pre-rich-types the binder accepted a boxed int for an enum-typed property because
        ///     the CLR's compiled-expression setter does <c>unbox.any TEnum</c> on a boxed integer
        ///     whose underlying type matches the enum. That silent coercion masked wire-format
        ///     bugs (e.g. a payload carrying a raw integer for an enum-typed property would land
        ///     as a "valid" enum value). Post-rich-types the codec produces typed enum values at
        ///     the JSON/FB → CLR boundary, so a boxed int reaching the binder is now always a
        ///     bug upstream. The binder rejects loudly.
        /// </summary>
        [TestMethod]
        public void RejectsIntOnEnumPropertyWithDescriptiveException()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<TestLogicBlock>();

            var ex = Assert.ThrowsExactly<ArgumentException>(() => binder.SetPropertyValue("TestDevice", "Mode", 1));
            StringAssert.Contains(ex.Message, "Mode");
            StringAssert.Contains(ex.Message, "enum");
        }

        [TestMethod]
        public void RejectsIntOnNullableEnumProperty()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            Assert.ThrowsExactly<ArgumentException>(() => binder.SetPropertyValue(ServiceId, "PreferredMode", 1));
        }

        [TestMethod]
        public void RejectsNullOnNonNullableEnumProperty()
        {
            // TestDevice.Mode is non-nullable OperatingMode — null is not a valid value.
            var (binder, _) = ServiceBinderTestHarness.Bind<TestLogicBlock>();

            Assert.ThrowsExactly<ArgumentException>(() => binder.SetPropertyValue("TestDevice", "Mode", null));
        }

        [TestMethod]
        public void AcceptsTypedEnumOnNonNullableEnumProperty()
        {
            // The canonical path: cloud sends "Manual" (enum member name) → codec produces
            // OperatingMode.Manual → binder receives the typed value → setter assigns directly.
            var (binder, block) = ServiceBinderTestHarness.Bind<TestLogicBlock>();

            binder.SetPropertyValue("TestDevice", "Mode", OperatingMode.Manual);

            Assert.AreEqual(OperatingMode.Manual, binder.GetPropertyValue("TestDevice", "Mode"));
            Assert.AreEqual(OperatingMode.Manual, block.Mode);
        }

        // ===================================================================
        // Nullable enum
        // ===================================================================

        [TestMethod]
        public void RoundTrip_NullableEnum_NullThenTypedValue()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "PreferredMode", null);
            Assert.IsNull(binder.GetPropertyValue(ServiceId, "PreferredMode"));

            binder.SetPropertyValue(ServiceId, "PreferredMode", OperatingMode.Auto);
            Assert.AreEqual(OperatingMode.Auto, binder.GetPropertyValue(ServiceId, "PreferredMode"));
        }

        // ===================================================================
        // Struct (Coordinates) — nullable writable property
        // ===================================================================

        [TestMethod]
        public void RoundTrip_NullableStruct_ValueThenNull()
        {
            var (binder, block) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();
            var coords = new Coordinates(47.3, 8.5);

            binder.SetPropertyValue(ServiceId, "PreferredLocation", coords);
            Assert.AreEqual(coords, binder.GetPropertyValue(ServiceId, "PreferredLocation"));
            Assert.AreEqual(coords, block.PreferredLocation);

            binder.SetPropertyValue(ServiceId, "PreferredLocation", null);
            Assert.IsNull(binder.GetPropertyValue(ServiceId, "PreferredLocation"));
            Assert.IsNull(block.PreferredLocation);
        }

        // ===================================================================
        // ImmutableArray<T> — writable service property
        // ===================================================================

        [TestMethod]
        public void RoundTrip_ImmutableArray_Elements()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();
            var setpoints = ImmutableArray.Create(1.0, 2.0, 3.0);

            binder.SetPropertyValue(ServiceId, "Setpoints", setpoints);

            var result = (ImmutableArray<double>)binder.GetPropertyValue(ServiceId, "Setpoints")!;
            Assert.HasCount(3, result);
            Assert.AreEqual(1.0, result[0]);
            Assert.AreEqual(2.0, result[1]);
            Assert.AreEqual(3.0, result[2]);
        }

        [TestMethod]
        public void RoundTrip_ImmutableArray_Empty()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            binder.SetPropertyValue(ServiceId, "Setpoints", ImmutableArray<double>.Empty);

            var result = (ImmutableArray<double>)binder.GetPropertyValue(ServiceId, "Setpoints")!;
            Assert.IsEmpty(result);
        }

        // ===================================================================
        // GetMeasuringPointValue — read-only measuring point
        // ===================================================================

        [TestMethod]
        public void GetMeasuringPointValue_ReturnsDefaultValueInitially()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            // Location is a Coordinates struct; default is Coordinates(0.0, 0.0).
            var value = binder.GetMeasuringPointValue(ServiceId, "Location");
            Assert.AreEqual(new Coordinates(0.0, 0.0), value);
        }

        [TestMethod]
        public void GetMeasuringPointValue_ReturnsCorrectValueAfterBackingFieldChange()
        {
            var (binder, block) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            // Measuring points are read-only via the binder; the backing field can only be
            // written by the block itself. Use reflection to simulate the block setting it.
            var prop = typeof(RichTypesLogicBlock).GetProperty("Location")!;
            prop.SetValue(block, new Coordinates(51.5, -0.1));

            var value = binder.GetMeasuringPointValue(ServiceId, "Location");
            Assert.AreEqual(new Coordinates(51.5, -0.1), value);
        }

        // ===================================================================
        // Error cases
        // ===================================================================

        [TestMethod]
        public void SetPropertyValue_ThrowsInvalidOperationException_WhenPropertyIsMeasuringPoint()
        {
            // Location is a [ServiceMeasuringPoint] — no setter registered in the binder.
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            Assert.ThrowsExactly<InvalidOperationException>(() => binder.SetPropertyValue(ServiceId, "Location", new Coordinates(0.0, 0.0)));
        }

        [TestMethod]
        public void SetPropertyValue_ThrowsInvalidOperationException_WhenPropertyDoesNotExist()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            Assert.ThrowsExactly<InvalidOperationException>(() => binder.SetPropertyValue(ServiceId, "DoesNotExist", 42));
        }

        [TestMethod]
        public void GetPropertyValue_ThrowsInvalidOperationException_WhenPropertyDoesNotExist()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            Assert.ThrowsExactly<InvalidOperationException>(() => binder.GetPropertyValue(ServiceId, "DoesNotExist"));
        }

        [TestMethod]
        public void GetMeasuringPointValue_ThrowsInvalidOperationException_WhenPointDoesNotExist()
        {
            var (binder, _) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();

            Assert.ThrowsExactly<InvalidOperationException>(() => binder.GetMeasuringPointValue(ServiceId, "DoesNotExist"));
        }
    }
}