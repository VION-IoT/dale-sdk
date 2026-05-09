using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Test.Configuration.Services;
using Vion.Dale.Sdk.Test.Introspection;

namespace Vion.Dale.Sdk.Test.Persistence
{
    /// <summary>
    ///     Regression tests for <see cref="PersistentData.Apply" /> against the JSON-roundtrip
    ///     shape that <c>JsonFilePersistentDataStore</c> in dale produces. The store
    ///     deserializes the file as <c>Dictionary&lt;string, List&lt;PersistentDataEntry&gt;&gt;</c>,
    ///     which leaves each entry's <c>Value</c> as a <see cref="JsonElement" />. Before
    ///     the fix, <see cref="PersistentData" /> assumed the value was already typed and
    ///     handed it straight to <c>ServiceBinder</c>'s compiled setter — which threw
    ///     <c>InvalidCastException</c> on every compound type (arrays, structs, enums after
    ///     a JsonStringEnumConverter pass), leaving those properties unrestored on every
    ///     dale boot.
    /// </summary>
    [TestClass]
    public sealed class PersistentDataShould
    {
        // The exact JsonSerializerOptions JsonFilePersistentDataStore uses on disk.
        // Mirror them here so the test JsonElements look like what comes off disk.
        private static readonly JsonSerializerOptions DiskOptions = new()
                                                                    {
                                                                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                                                                    };

        [TestMethod]
        public void RestorePrimitiveDoubleFromJsonElement()
        {
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.VoltageSetpoint", typeof(double).FullName!, ToJsonElement(230.5)),
            ]);

            Assert.AreEqual(230.5, block.VoltageSetpoint);
        }

        [TestMethod]
        public void RestoreNullableDoubleFromJsonElement()
        {
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.Target", typeof(double?).FullName!, ToJsonElement(7.0)),
            ]);

            Assert.AreEqual(7.0, block.Target);
        }

        [TestMethod]
        public void RestoreImmutableArrayOfDoubleFromJsonElement()
        {
            // The bug-fix regression test. Pre-fix this would throw InvalidCastException:
            // "Unable to cast object of type 'System.Text.Json.JsonElement' to type
            //  'System.Collections.Immutable.ImmutableArray`1[System.Double]'".
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.Setpoints",
                                        typeof(ImmutableArray<double>).FullName!,
                                        ToJsonElement(new[] { 1.1, 2.2, 3.3 })),
            ]);

            CollectionAssert.AreEqual(new[] { 1.1, 2.2, 3.3 }, block.Setpoints);
        }

        [TestMethod]
        public void RestoreNullableStructFromJsonElement()
        {
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.PreferredLocation",
                                        typeof(Coordinates?).FullName!,
                                        ToJsonElement(new Coordinates(47.3, 8.5))),
            ]);

            Assert.IsNotNull(block.PreferredLocation);
            Assert.AreEqual(47.3, block.PreferredLocation!.Value.Lat);
            Assert.AreEqual(8.5, block.PreferredLocation!.Value.Lon);
        }

        [TestMethod]
        public void RestoreNullableStructAsNullFromJsonElement()
        {
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.PreferredLocation",
                                        typeof(Coordinates?).FullName!,
                                        ToJsonElement<Coordinates?>(null)),
            ]);

            Assert.IsNull(block.PreferredLocation);
        }

        [TestMethod]
        public void RestoreImmutableArrayOfStructFromJsonElement()
        {
            var (block, persistentData) = SetUp();
            var schedule = new[]
                           {
                               new ScheduledSetpoint(new System.DateTime(2026, 5, 4, 0, 0, 0, System.DateTimeKind.Utc), 5.0, 230.0),
                               new ScheduledSetpoint(new System.DateTime(2026, 5, 5, 0, 0, 0, System.DateTimeKind.Utc), 6.0, 231.0),
                           };

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.Schedule",
                                        typeof(ImmutableArray<ScheduledSetpoint>).FullName!,
                                        ToJsonElement(schedule)),
            ]);

            Assert.HasCount(2, block.Schedule);
            Assert.AreEqual(5.0, block.Schedule[0].PowerSetpoint);
            Assert.AreEqual(231.0, block.Schedule[1].VoltageSetpoint);
        }

        [TestMethod]
        public void RestoreNullableEnumFromJsonElementAsString()
        {
            // JsonStringEnumConverter writes enums as their member name. The pre-fix code
            // would NRE / InvalidCast since the JsonElement was not the enum's CLR type.
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.PreferredMode",
                                        typeof(OperatingMode?).FullName!,
                                        ToJsonElement(OperatingMode.Manual)),
            ]);

            Assert.AreEqual(OperatingMode.Manual, block.PreferredMode);
        }

        [TestMethod]
        public void RestoreAlreadyTypedPrimitive()
        {
            // Storage layers (or test code) may pre-coerce primitives. The fix path must
            // recognise an already-typed value and pass it through unchanged.
            var (block, persistentData) = SetUp();

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.VoltageSetpoint", typeof(double).FullName!, 99.5),
            ]);

            Assert.AreEqual(99.5, block.VoltageSetpoint);
        }

        [TestMethod]
        public void TolerateNullValueOnNullableProperty()
        {
            var (block, persistentData) = SetUp();
            // First set a non-null so the null assignment is observable as a change.
            block.Target = 5.0;

            persistentData.Apply([
                new PersistentDataEntry("RichDevice.Target", typeof(double?).FullName!, Value: null!),
            ]);

            Assert.IsNull(block.Target);
        }

        [TestMethod]
        public void IgnoreUnknownPropertyKeyWithoutThrowing()
        {
            var (block, persistentData) = SetUp();

            // Should not throw — the implementation logs a warning and moves on so
            // a stale persistence file from an older logic-block schema doesn't fail boot.
            persistentData.Apply([
                new PersistentDataEntry("RichDevice.PropertyThatNoLongerExists",
                                        typeof(int).FullName!,
                                        ToJsonElement(42)),
            ]);

            // No assertion needed; success = no exception thrown.
        }

        // ─────────────────────────────────────────────────────────────────────

        private static (RichTypesLogicBlock Block, PersistentData PersistentData) SetUp()
        {
            var (binder, block) = ServiceBinderTestHarness.Bind<RichTypesLogicBlock>();
            var persistentData = new PersistentData();
            ILogger logger = NullLogger.Instance;
            persistentData.Initialize(block, binder, logger);
            return (block, persistentData);
        }

        private static JsonElement ToJsonElement<T>(T value)
        {
            // Round-trips through JsonSerializer with the same options
            // JsonFilePersistentDataStore uses on disk, so the JsonElement looks
            // identical to what dale loads on boot.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, DiskOptions);
            return JsonSerializer.Deserialize<JsonElement>(bytes, DiskOptions);
        }
    }
}
