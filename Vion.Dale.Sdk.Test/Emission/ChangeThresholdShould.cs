using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The six deadbands the SDK ships built in, reached the way the gate reaches them — boxed, through
    ///     the non-generic adapter. Each clears when the magnitude of the change is at least the configured
    ///     threshold, so the boundary value emits and everything below it is suppressed.
    /// </summary>
    [TestClass]
    public class ChangeThresholdShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        [DataRow(typeof(double), 10.0d, 13.5d, "2", DisplayName = "double above the threshold")]
        [DataRow(typeof(double), 10.0d, 12.0d, "2", DisplayName = "double at the threshold")]
        [DataRow(typeof(float), 1.0f, 1.5f, "0.5", DisplayName = "float at the threshold")]
        [DataRow(typeof(int), 10, 15, "5", DisplayName = "int at the threshold")]
        [DataRow(typeof(long), 1_000L, 1_100L, "100", DisplayName = "long at the threshold")]
        public void ClearAThresholdTheChangeReaches(Type valueType, object lastEmitted, object candidate, string threshold)
        {
            // Arrange
            var adapter = Adapter(valueType);

            // Act
            var cleared = adapter.Exceeds(lastEmitted, candidate, threshold);

            // Assert
            Assert.IsTrue(cleared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        [DataRow(typeof(double), 10.0d, 11.0d, "2", DisplayName = "double below")]
        [DataRow(typeof(float), 1.0f, 1.25f, "0.5", DisplayName = "float below")]
        [DataRow(typeof(int), 10, 13, "5", DisplayName = "int below")]
        [DataRow(typeof(long), 1_000L, 1_050L, "100", DisplayName = "long below")]
        public void HoldAThresholdTheChangeDoesNotReach(Type valueType, object lastEmitted, object candidate, string threshold)
        {
            // Arrange
            var adapter = Adapter(valueType);

            // Act
            var cleared = adapter.Exceeds(lastEmitted, candidate, threshold);

            // Assert
            Assert.IsFalse(cleared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        [DataRow(typeof(double), 10.0d, 7.0d, "2", DisplayName = "double falling")]
        [DataRow(typeof(float), 1.5f, 1.0f, "0.5", DisplayName = "float falling")]
        [DataRow(typeof(int), 15, 10, "5", DisplayName = "int falling")]
        [DataRow(typeof(long), 1_100L, 1_000L, "100", DisplayName = "long falling")]
        public void CompareTheMagnitudeOfAFallingChange(Type valueType, object lastEmitted, object candidate, string threshold)
        {
            // Arrange
            var adapter = Adapter(valueType);

            // Act
            var cleared = adapter.Exceeds(lastEmitted, candidate, threshold);

            // Assert
            Assert.IsTrue(cleared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        public void CompareDecimalsWithoutLosingPrecision()
        {
            // Arrange — decimal has no DataRow-able literal, so it gets its own rows.
            var adapter = Adapter(typeof(decimal));

            // Act / Assert
            Assert.IsTrue(adapter.Exceeds(100m, 90m, "10"));
            Assert.IsFalse(adapter.Exceeds(100m, 95m, "10"));
            Assert.IsTrue(adapter.Exceeds(90m, 80m, "10"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        public void CompareLongsAcrossOppositeSignExtremes()
        {
            // Arrange — the delta overflows Int64 arithmetic, so the comparison must widen.
            var adapter = Adapter(typeof(long));

            // Act
            var cleared = adapter.Exceeds(long.MinValue, long.MaxValue, "1");

            // Assert
            Assert.IsTrue(cleared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.2")]
        [DataRow(1000, 2000, "1s", true, DisplayName = "a second, reached")]
        [DataRow(1000, 1500, "1s", false, DisplayName = "a second, not reached")]
        [DataRow(5000, 2000, "3s", true, DisplayName = "falling by three seconds")]
        [DataRow(0, 250, "250ms", true, DisplayName = "milliseconds, reached")]
        [DataRow(0, 249, "250ms", false, DisplayName = "milliseconds, not reached")]
        public void ReadADurationThresholdWithTheDurationGrammar(int lastEmittedMilliseconds, int candidateMilliseconds, string threshold, bool expectedToClear)
        {
            // Arrange
            var adapter = Adapter(typeof(TimeSpan));

            // Act
            var cleared = adapter.Exceeds(TimeSpan.FromMilliseconds(lastEmittedMilliseconds), TimeSpan.FromMilliseconds(candidateMilliseconds), threshold);

            // Assert
            Assert.AreEqual(expectedToClear, cleared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.2")]
        public void ReadADurationThresholdBelowTheMillisecond()
        {
            // Arrange — 500us is 5000 ticks; the grammar's sub-millisecond unit must survive the round trip.
            var adapter = Adapter(typeof(TimeSpan));

            // Act / Assert
            Assert.IsTrue(adapter.Exceeds(TimeSpan.Zero, TimeSpan.FromTicks(5000), "500us"));
            Assert.IsFalse(adapter.Exceeds(TimeSpan.Zero, TimeSpan.FromTicks(4999), "500us"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-006.3")]
        public void ClearTheThresholdWhenEitherSideIsAbsent()
        {
            // Arrange — a null has no magnitude, so the first real value after one must not be suppressed.
            var adapter = Adapter(typeof(double));

            // Act / Assert
            Assert.IsTrue(adapter.Exceeds(null, 10.0d, "1000"));
            Assert.IsTrue(adapter.Exceeds(10.0d, null, "1000"));
            Assert.IsTrue(adapter.Exceeds(null, null, "1000"));
        }

        private static IChangeThresholdAdapter Adapter(Type valueType)
        {
            Assert.IsTrue(ChangeThresholdRegistry.TryResolve(valueType, null, out var adapter), $"no built-in deadband for {valueType.Name}");
            return adapter;
        }
    }
}
