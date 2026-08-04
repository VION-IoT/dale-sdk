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
        public void Describe_NumberWithFormat_UsesFormat()
        {
            // title on a primitive is a display name, not a type identity — it must be ignored.
            var schema = Parse("""{ "type": "number", "format": "double", "title": "Spannungs-Sollwert", "x-unit": "V" }""");

            Assert.AreEqual("double", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_PrimitiveWithoutFormat_UsesType()
        {
            Assert.AreEqual("string", SchemaSummary.Describe(Parse("""{ "type": "string" }""")));
            Assert.AreEqual("boolean", SchemaSummary.Describe(Parse("""{ "type": "boolean" }""")));
        }

        [TestMethod]
        public void Describe_StringFormats_SurfaceThePreciseKind()
        {
            Assert.AreEqual("duration", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "duration" }""")));
            Assert.AreEqual("date-time", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "date-time" }""")));
            Assert.AreEqual("uuid", SchemaSummary.Describe(Parse("""{ "type": "string", "format": "uuid" }""")));
        }

        [TestMethod]
        public void Describe_NullablePrimitive_AppendsQuestionMark()
        {
            var schema = Parse("""{ "type": ["number", "null"], "format": "double" }""");

            Assert.AreEqual("double?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_StructWithTitle_UsesIdentityTitle()
        {
            var schema = Parse("""{ "type": "object", "title": "Coordinates", "properties": {} }""");

            Assert.AreEqual("Coordinates", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_NullableStruct_AppendsQuestionMark()
        {
            var schema = Parse("""{ "type": ["object", "null"], "title": "ScheduledSetpoint" }""");

            Assert.AreEqual("ScheduledSetpoint?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_ObjectWithoutTitle_FallsBackToObject()
        {
            Assert.AreEqual("object", SchemaSummary.Describe(Parse("""{ "type": "object" }""")));
        }

        [TestMethod]
        public void Describe_EnumWithTitle_UsesIdentityTitle()
        {
            var schema = Parse("""{ "type": "string", "title": "AlarmState", "enum": ["Ok", "Warning", "Critical"] }""");

            Assert.AreEqual("AlarmState", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_NullableEnum_AppendsQuestionMark()
        {
            var schema = Parse("""{ "type": ["string", "null"], "title": "AlarmState", "enum": ["Ok", "Warning", "Critical", null] }""");

            Assert.AreEqual("AlarmState?", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_EnumWithoutTitle_FallsBackToEnum()
        {
            Assert.AreEqual("enum", SchemaSummary.Describe(Parse("""{ "type": "string", "enum": ["A", "B"] }""")));
        }

        [TestMethod]
        public void Describe_ArrayOfStruct_UsesItemSummaryWithBrackets()
        {
            var schema = Parse("""{ "type": "array", "items": { "type": "object", "title": "ScheduledSetpoint" } }""");

            Assert.AreEqual("ScheduledSetpoint[]", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_ArrayOfNullablePrimitive_UsesItemSummaryWithBrackets()
        {
            var schema = Parse("""{ "type": "array", "items": { "type": ["number", "null"], "format": "double" } }""");

            Assert.AreEqual("double?[]", SchemaSummary.Describe(schema));
        }

        [TestMethod]
        public void Describe_ArrayWithoutItems_FallsBackToArray()
        {
            Assert.AreEqual("array", SchemaSummary.Describe(Parse("""{ "type": "array" }""")));
        }

        [TestMethod]
        public void Describe_MissingOrDegenerateSchema_ReturnsEmpty()
        {
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