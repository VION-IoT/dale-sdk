using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The one grammar both duration knobs are written in — a member's <c>MinInterval</c>, and a
    ///     <c>TimeSpan</c> member's <c>MinChange</c>. Every rejection it makes reaches the author twice:
    ///     as a diagnostic at compile time and as a refused block start when that was suppressed.
    /// </summary>
    [TestClass]
    public class DurationParserShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        [DataRow("500us", 5_000L, DisplayName = "microseconds, in ticks of 100 ns")]
        [DataRow("250ms", 2_500_000L, DisplayName = "milliseconds")]
        [DataRow("30s", 300_000_000L, DisplayName = "seconds")]
        [DataRow("5m", 3_000_000_000L, DisplayName = "minutes")]
        [DataRow("2h", 72_000_000_000L, DisplayName = "hours")]
        [DataRow("250", 2_500_000L, DisplayName = "a bare number is milliseconds")]
        [DataRow("0", 0L, DisplayName = "the disabling sentinel")]
        [DataRow("0ms", 0L, DisplayName = "the disabling sentinel with its unit")]
        [DataRow("1.5s", 15_000_000L, DisplayName = "a fractional value")]
        [DataRow("250MS", 2_500_000L, DisplayName = "the suffix is case-insensitive")]
        [DataRow(" 250ms ", 2_500_000L, DisplayName = "surrounding whitespace is tolerated")]
        public void ReadNumberAndOptionalUnit(string token, long expectedTicks)
        {
            // Arrange / Act
            var duration = DurationParser.Parse(token);

            // Assert
            Assert.AreEqual(TimeSpan.FromTicks(expectedTicks), duration);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        [DataRow("", DisplayName = "empty")]
        [DataRow("   ", DisplayName = "whitespace only")]
        [DataRow("ms", DisplayName = "a unit with no number")]
        [DataRow("10x", DisplayName = "an unknown unit")]
        public void RejectTokenItCannotRead(string token)
        {
            // Arrange / Act / Assert
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(token));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        public void NameTheTokenItRejects()
        {
            // Arrange / Act
            var rejection = Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("ms"));

            // Assert — the author has to find the knob from the message alone.
            StringAssert.Contains(rejection.Message, "ms");
            StringAssert.Contains(rejection.Message, "no numeric part");
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.3")]
        [DataRow("-1s")]
        [DataRow("-250ms")]
        [DataRow("-5")]
        public void RejectNegativeDuration(string token)
        {
            // Arrange / Act / Assert — a spacing has no negative direction, and accepting one would make
            // the gate it configures unconditional instead of restrictive.
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(token));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.4")]
        [DataRow("999999999999999999999h", DisplayName = "hours beyond the range")]
        [DataRow("99999999999999999999999999", DisplayName = "milliseconds beyond the range")]
        public void RejectDurationTooLargeToRepresent(string token)
        {
            // Arrange / Act / Assert — rejected as the malformed knob it is, rather than surfacing as an
            // arithmetic fault from deep inside the parse.
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(token));
        }
    }
}