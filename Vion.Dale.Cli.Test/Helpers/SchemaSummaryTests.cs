using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    /// <summary>
    ///     The compact type label `dale list` derives from a property's introspection schema
    ///     (JSON Schema 2020-12, Dale profile). Shapes taken from real parser output.
    /// </summary>
    [TestClass]
    public class SchemaSummaryTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_NumberWithFormat_UsesFormat()
        {
            // Arrange / Act
            // title on a primitive is a display name, not a type identity — it must be ignored.
            var schema = Parse("""{ "type": "number", "format": "double", "title": "Spannungs-Sollwert", "x-unit": "V" }""");

            // Assert
            Assert.AreEqual("double", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_PrimitiveWithoutFormat_UsesType()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual("string", SchemaSummary.Describe(Parse("""{ "type": "string" }""")));
            Assert.AreEqual("boolean", SchemaSummary.Describe(Parse("""{ "type": "boolean" }""")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_StringFormats_SurfacePreciseKind()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual("duration", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "duration" }""")));
            Assert.AreEqual("date-time", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "date-time" }""")));
            Assert.AreEqual("uuid", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "uuid" }""")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_NullablePrimitive_AppendsQuestionMark()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": ["number", "null"], "format": "double" }""");

            // Assert
            Assert.AreEqual("double?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_StructWithTitle_UsesIdentityTitle()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": "object", "title": "Coordinates", "properties": {} }""");

            // Assert
            Assert.AreEqual("Coordinates", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_NullableStruct_AppendsQuestionMark()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": ["object", "null"], "title": "ScheduledSetpoint" }""");

            // Assert
            Assert.AreEqual("ScheduledSetpoint?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_ObjectWithoutTitle_FallsBackToObject()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual("object", SchemaSummary.Describe(Parse("""{ "type": "object" }""")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_EnumWithTitle_UsesIdentityTitle()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": "string", "title": "AlarmState", "enum": ["Ok", "Warning", "Critical"] }""");

            // Assert
            Assert.AreEqual("AlarmState", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_NullableEnum_AppendsQuestionMark()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": ["string", "null"], "title": "AlarmState", "enum": ["Ok", "Warning", "Critical", null] }""");

            // Assert
            Assert.AreEqual("AlarmState?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_EnumWithoutTitle_FallsBackToEnum()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual("enum", SchemaSummary.Describe(Parse("""{ "type": "string", "enum": ["A", "B"] }""")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_ArrayOfStruct_UsesItemSummaryWithBrackets()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": "array", "items": { "type": "object", "title": "ScheduledSetpoint" } }""");

            // Assert
            Assert.AreEqual("ScheduledSetpoint[]", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_ArrayOfNullablePrimitive_UsesItemSummaryWithBrackets()
        {
            // Arrange / Act
            var schema = Parse("""{ "type": "array", "items": { "type": ["number", "null"], "format": "double" } }""");

            // Assert
            Assert.AreEqual("double?[]", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_ArrayWithoutItems_FallsBackToArray()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual("array", SchemaSummary.Describe(Parse("""{ "type": "array" }""")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void Describe_MissingOrDegenerateSchema_ReturnsEmpty()
        {
            // Arrange / Act

            // Assert
            Assert.AreEqual(string.Empty, SchemaSummary.Describe(null));
            Assert.AreEqual(string.Empty, SchemaSummary.Describe(Parse("{}")));
            Assert.AreEqual(string.Empty, SchemaSummary.Describe(JsonValue.Create(true)));
        }

        private static JsonNode Parse(string json)
        {
            return JsonNode.Parse(json)!;
        }
    }
}