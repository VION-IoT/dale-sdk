using System.Text.Json;
using Vion.Contracts.Hw;
using Vion.Contracts.Hw.Ao;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     The SDK's own serializer options and the <c>hw/*</c> source-generation context are two paths onto
    ///     one wire: the reflection-based <c>PublishJson</c> / <c>GetJsonPayload</c> overloads use these
    ///     options, the typed overloads use <see cref="HwJsonContext" />, and a hardware-abstraction layer
    ///     reads whichever produced the document. This suite pins that the two agree on the shapes where
    ///     they could silently diverge.
    ///     <para>
    ///         These are <b>premise</b> tests and cite no acceptance criterion by design
    ///         (<c>testing-conventions.md</c> § 17): no consumer observes "two code paths stay in step", and
    ///         the criteria the paths serve — a value carried unaltered, a camel-cased name — are stated and
    ///         proven per contract family on their own pages.
    ///     </para>
    /// </summary>
    [TestClass]
    public class JsonSerializationShould
    {
        [TestMethod]
        [DataRow(double.NaN, DisplayName = "not a number")]
        [DataRow(double.PositiveInfinity, DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, DisplayName = "negative infinity")]
        [DataRow(42.5, DisplayName = "an ordinary setpoint")]
        public void WriteValueTheSharedContextReads(double value)
        {
            // Arrange
            var written = JsonSerializer.Serialize(new AoStatePayload(value), JsonSerialization.DefaultOptions);

            // Act
            var read = JsonSerializer.Deserialize(written, HwJsonContext.Default.AoStatePayload);

            // Assert
            Assert.AreEqual(value, read!.Value);
        }

        [TestMethod]
        [DataRow(double.NaN, DisplayName = "not a number")]
        [DataRow(double.PositiveInfinity, DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, DisplayName = "negative infinity")]
        [DataRow(42.5, DisplayName = "an ordinary setpoint")]
        public void ReadValueTheSharedContextWrites(double value)
        {
            // Arrange
            var written = JsonSerializer.Serialize(new AoStatePayload(value), HwJsonContext.Default.AoStatePayload);

            // Act
            var read = JsonSerializer.Deserialize<AoStatePayload>(written, JsonSerialization.DefaultOptions);

            // Assert
            Assert.AreEqual(value, read.Value);
        }

        [TestMethod]
        [DataRow(double.NaN, """{"value":"NaN"}""", DisplayName = "not a number")]
        [DataRow(double.PositiveInfinity, """{"value":"Infinity"}""", DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, """{"value":"-Infinity"}""", DisplayName = "negative infinity")]
        public void WriteNonFiniteValueAsQuotedNamedLiteral(double value, string expectedDocument)
        {
            // Arrange / Act — the document itself, because the round trips above agree on any spelling the
            // two paths share, including one no other reader of this wire accepts. The named literals are
            // quoted: a non-finite value leaves as a JSON string, where every finite one leaves as a number.
            var written = JsonSerializer.Serialize(new AoStatePayload(value), JsonSerialization.DefaultOptions);

            // Assert
            Assert.AreEqual(expectedDocument, written);
        }
    }
}