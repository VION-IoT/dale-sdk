using System;
using System.Text.Json;
using Vion.Dale.DevHost.Web.Api.Serialization;

namespace Vion.Dale.DevHost.Test.Api.Serialization
{
    /// <summary>
    ///     The duration converter's read half. A duration the wire form does not define is a malformed
    ///     body, so the decode raises the JSON error class the input pipeline turns into a per-call
    ///     refusal; a parse exception escaping it reaches the caller as a server fault instead.
    /// </summary>
    [TestClass]
    public class Iso8601TimeSpanConverterShould
    {
        private static readonly JsonSerializerOptions Options = new() { Converters = { new Iso8601TimeSpanConverter() } };

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.6")]
        [DataRow("\"nope\"", "nope", DisplayName = "neither wire form")]
        [DataRow("\"PT\"", "PT", DisplayName = "ISO prefix with no components")]
        [DataRow("\"10675200.00:00:00\"", "10675200.00:00:00", DisplayName = "beyond the representable range")]
        public void RefuseUnrepresentableDurationNamingOfferedText(string body, string offered)
        {
            // Arrange / Act
            var refusal = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>(body, Options));

            // Assert
            StringAssert.Contains(refusal.Message, offered);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-014.5")]
        [DataRow("\"PT5S\"", 5)]
        [DataRow("\"00:00:07\"", 7)]
        [DataRow("\"\"", 0)]
        public void ReadBothDurationFormsAndEmptyText(string body, int expectedSeconds)
        {
            // Arrange / Act
            var value = JsonSerializer.Deserialize<TimeSpan>(body, Options);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), value);
        }
    }
}