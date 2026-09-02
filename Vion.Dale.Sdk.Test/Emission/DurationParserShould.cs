using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class DurationParserShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseMicroseconds()
        {
            // 1 microsecond = 10 ticks (1 tick = 100 ns).
            Assert.AreEqual(TimeSpan.FromTicks(5000), DurationParser.Parse("500us"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseMilliseconds()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), DurationParser.Parse("250ms"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseSeconds()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), DurationParser.Parse("30s"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseMinutes()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(5), DurationParser.Parse("5m"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseHours()
        {
            Assert.AreEqual(TimeSpan.FromHours(2), DurationParser.Parse("2h"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseBareNumberAsMilliseconds()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), DurationParser.Parse("250"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.1")]
        public void ParseBareZeroAsZeroMilliseconds()
        {
            Assert.AreEqual(TimeSpan.Zero, DurationParser.Parse("0"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        public void ThrowOnEmptyString()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(""));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        public void ThrowOnWhitespaceString()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("   "));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        public void ThrowOnUnitOnlyTokenWithClearMessage()
        {
            // "ms" has no numeric part — should produce a clear FormatException, not a bare parse failure.
            var ex = Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("ms"));
            StringAssert.Contains(ex.Message, "ms");
            StringAssert.Contains(ex.Message, "no numeric part");
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.3")]
        [DataRow("-1s")]
        [DataRow("-250ms")]
        [DataRow("-5")]
        public void ThrowOnNegativeDuration(string token)
        {
            // Arrange / Act / Assert
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(token));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-007.2")]
        public void ThrowOnUnknownUnit()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("10x"));
        }
    }
}