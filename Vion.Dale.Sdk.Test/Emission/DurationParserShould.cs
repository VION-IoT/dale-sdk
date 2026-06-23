using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class DurationParserShould
    {
        [TestMethod]
        public void ParseMicroseconds()
        {
            // 1 microsecond = 10 ticks (1 tick = 100 ns).
            Assert.AreEqual(TimeSpan.FromTicks(5000), DurationParser.Parse("500us"));
        }

        [TestMethod]
        public void ParseMilliseconds()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), DurationParser.Parse("250ms"));
        }

        [TestMethod]
        public void ParseSeconds()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), DurationParser.Parse("30s"));
        }

        [TestMethod]
        public void ParseMinutes()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(5), DurationParser.Parse("5m"));
        }

        [TestMethod]
        public void ParseHours()
        {
            Assert.AreEqual(TimeSpan.FromHours(2), DurationParser.Parse("2h"));
        }

        [TestMethod]
        public void ParseBareNumberAsMilliseconds()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), DurationParser.Parse("250"));
        }

        [TestMethod]
        public void ParseBareZeroAsZeroMilliseconds()
        {
            Assert.AreEqual(TimeSpan.Zero, DurationParser.Parse("0"));
        }

        [TestMethod]
        public void ThrowOnEmptyString()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse(""));
        }

        [TestMethod]
        public void ThrowOnWhitespaceString()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("   "));
        }

        [TestMethod]
        public void ThrowOnUnitOnlyTokenWithClearMessage()
        {
            // "ms" has no numeric part — should produce a clear FormatException, not a bare parse failure.
            var ex = Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("ms"));
            StringAssert.Contains(ex.Message, "ms");
            StringAssert.Contains(ex.Message, "no numeric part");
        }

        [TestMethod]
        public void ThrowOnUnknownUnit()
        {
            Assert.ThrowsExactly<FormatException>(() => DurationParser.Parse("10x"));
        }
    }
}