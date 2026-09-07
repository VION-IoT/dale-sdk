using System;
using System.Text.Json;
using Vion.Dale.DevHost.Web.Api.Serialization;

namespace Vion.Dale.DevHost.Test.Api.Serialization
{
    /// <summary>
    ///     The duration converter's read half. A duration the wire form does not define is a malformed
    ///     payload, so the decode raises the JSON error class the input pipeline turns into a per-call
    ///     refusal; a parse exception escaping it reaches the caller as a server fault instead. An
    ///     absent duration is not malformed — it reads as zero, which is why the criteria split.
    /// </summary>
    [TestClass]
    public class Iso8601TimeSpanConverterShould
    {
        private static readonly JsonSerializerOptions Options = new() { Converters = { new Iso8601TimeSpanConverter() } };

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.6")]
        [DataRow("\"PT5S\"", 5, DisplayName = "the ISO form")]
        [DataRow("\"00:00:07\"", 7, DisplayName = "the .NET form")]
        [DataRow("\"\"", 0, DisplayName = "absent as empty text")]
        [DataRow("null", 0, DisplayName = "absent as null")]
        public void ReadEitherDurationFormAndAbsentAsZero(string body, int expectedSeconds)
        {
            // Arrange / Act
            var value = JsonSerializer.Deserialize<TimeSpan>(body, Options);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.7")]
        [DataRow("\"nope\"", "nope", DisplayName = "neither wire form")]
        [DataRow("\"PT\"", "PT", DisplayName = "ISO prefix with no components")]
        [DataRow("\"10675200.00:00:00\"", "10675200.00:00:00", DisplayName = ".NET form beyond the representable range")]
        [DataRow("\"P100000000D\"", "P100000000D", DisplayName = "ISO form beyond the representable range")]
        public void RefuseUnrepresentableDurationNamingOfferedText(string body, string offered)
        {
            // Arrange / Act
            var refusal = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>(body, Options));

            // Assert
            StringAssert.Contains(refusal.Message, offered);
        }
    }
}