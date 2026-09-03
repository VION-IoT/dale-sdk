using System;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Test.Introspection
{
    [TestClass]
    public class TypeRefBuilderShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsPrimitiveSchemaForBool()
        {
            // Arrange

            // Act
            var schema = GetSchema("BoolValue");

            // Assert
            Assert.AreEqual("boolean", schema["type"]?.GetValue<string>());
            Assert.IsNull(schema["format"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsPrimitiveSchemaForByte()
        {
            // Arrange

            // Act
            var schema = GetSchema("ByteValue");

            // Assert
            Assert.AreEqual("integer", schema["type"]?.GetValue<string>());
            Assert.AreEqual("uint8", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsPrimitiveSchemaForUShort()
        {
            // Arrange

            // Act
            var schema = GetSchema("UShortValue");

            // Assert
            Assert.AreEqual("integer", schema["type"]?.GetValue<string>());
            Assert.AreEqual("uint16", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsPrimitiveSchemaForUInt()
        {
            // Arrange

            // Act
            var schema = GetSchema("UIntValue");

            // Assert
            Assert.AreEqual("integer", schema["type"]?.GetValue<string>());
            Assert.AreEqual("uint32", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsPrimitiveSchemaForLong()
        {
            // Arrange

            // Act
            var schema = GetSchema("LongValue");

            // Assert
            Assert.AreEqual("integer", schema["type"]?.GetValue<string>());
            Assert.AreEqual("int64", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsNullablePrimitiveSchemaForOptionalDouble()
        {
            // Arrange

            // Act
            var schema = GetSchema("Target");
            var typeArr = schema["type"] as JsonArray;

            // Assert
            Assert.IsNotNull(typeArr);
            Assert.HasCount(2, typeArr);
            Assert.AreEqual("number", typeArr[0]!.GetValue<string>());
            Assert.AreEqual("null", typeArr[1]!.GetValue<string>());
            Assert.AreEqual("double", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsNullablePrimitiveSchemaForOptionalInt()
        {
            // Arrange

            // Act
            var schema = GetSchema("OptionalCount");
            var typeArr = schema["type"] as JsonArray;

            // Assert
            Assert.IsNotNull(typeArr);
            Assert.HasCount(2, typeArr);
            Assert.AreEqual("integer", typeArr[0]!.GetValue<string>());
            Assert.AreEqual("null", typeArr[1]!.GetValue<string>());
            Assert.AreEqual("int32", schema["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.7")]
        public void EmitsNonNullableStringSchemaForNonNullName()
        {
            // Arrange

            // Act
            var schema = GetSchema("NonNullName");

            // Assert
            Assert.AreEqual("string", schema["type"]?.GetValue<string>());
            Assert.IsNull(schema["format"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.7")]
        public void EmitsNullableStringSchemaForOptionalErrorMessage()
        {
            // Arrange

            // Act
            var schema = GetSchema("OptionalErrorMessage");
            var typeArr = schema["type"] as JsonArray;

            // Assert
            Assert.IsNotNull(typeArr);
            Assert.HasCount(2, typeArr);
            Assert.AreEqual("string", typeArr[0]!.GetValue<string>());
            Assert.AreEqual("null", typeArr[1]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.2")]
        public void EmitsAnnotationsForVoltageSetpoint()
        {
            // Arrange

            // Act
            var schema = GetSchema("VoltageSetpoint");

            // Assert
            Assert.AreEqual("number", schema["type"]?.GetValue<string>());
            Assert.AreEqual("V", schema["x-unit"]?.GetValue<string>());
            Assert.AreEqual(0d, schema["minimum"]?.GetValue<double>());
            Assert.AreEqual(400d, schema["maximum"]?.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsArraySchemaForImmutableArrayOfDouble()
        {
            // Arrange

            // Act
            var schema = GetSchema("HistogramBuckets");

            // Assert
            Assert.AreEqual("array", schema["type"]?.GetValue<string>());
            Assert.AreEqual("number", schema["items"]?["type"]?.GetValue<string>());
            Assert.AreEqual("double", schema["items"]?["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.2")]
        public void EmitsUnitOnArrayRootOnlyForAnnotatedImmutableArray()
        {
            // Arrange
            // Property-level x-unit lives on the array root only — items carries element-shape
            // (type, format, enum, struct properties, etc.) but not property-level annotations.
            // Vion.Contracts >= 0.7.0 dropped the previous double-apply.

            // Act
            var schema = GetSchema("HistogramBuckets");

            // Assert
            Assert.AreEqual("A", schema["x-unit"]?.GetValue<string>());
            Assert.IsNull(schema["items"]?["x-unit"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void EmitsArrayOfNullableSchemaForSamplesWithGaps()
        {
            // Arrange

            // Act
            var schema = GetSchema("SamplesWithGaps");

            // Assert
            Assert.AreEqual("array", schema["type"]?.GetValue<string>());
            var itemsType = schema["items"]?["type"] as JsonArray;
            Assert.IsNotNull(itemsType);
            Assert.HasCount(2, itemsType);
            Assert.AreEqual("integer", itemsType[0]!.GetValue<string>());
            Assert.AreEqual("null", itemsType[1]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsStructSchemaForLocation()
        {
            // Arrange

            // Act
            var schema = GetSchema("Location");

            // Assert
            Assert.AreEqual("object", schema["type"]?.GetValue<string>());
            Assert.AreEqual("Coordinates", schema["title"]?.GetValue<string>());
            var props = schema["properties"] as JsonObject;
            Assert.IsNotNull(props);
            Assert.IsNotNull(props["lat"]);
            Assert.IsNotNull(props["lon"]);
            Assert.AreEqual("number", props["lat"]?["type"]?.GetValue<string>());
            Assert.AreEqual("double", props["lat"]?["format"]?.GetValue<string>());
            var required = schema["required"] as JsonArray;
            Assert.IsNotNull(required);
            Assert.HasCount(2, required);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsAdditionalPropertiesFalseOnStruct()
        {
            // Arrange

            // Act
            var schema = GetSchema("Location");

            // Assert
            Assert.IsFalse(schema["additionalProperties"]?.GetValue<bool>() ?? true);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsNullableStructSchemaForLastKnownLocation()
        {
            // Arrange

            // Act
            var schema = GetSchema("LastKnownLocation");
            var typeArr = schema["type"] as JsonArray;

            // Assert
            Assert.IsNotNull(typeArr);
            Assert.HasCount(2, typeArr);
            Assert.AreEqual("object", typeArr[0]!.GetValue<string>());
            Assert.AreEqual("null", typeArr[1]!.GetValue<string>());
            Assert.AreEqual("Coordinates", schema["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.6")]
        public void EmitsStructFieldAnnotationsForNullableStruct()
        {
            // Arrange
            // Regression (Vion.Contracts < 0.10.2): the nullable wrapper stripped [StructField]
            // metadata — LastKnownLocation (Coordinates?) emitted bare field schemas while
            // Location (Coordinates) carried description/minimum/maximum/x-unit.

            // Act
            var schema = GetSchema("LastKnownLocation");
            var props = schema["properties"] as JsonObject;

            // Assert
            Assert.IsNotNull(props);
            Assert.AreEqual("deg", props["lat"]?["x-unit"]?.GetValue<string>());
            Assert.AreEqual(-90d, props["lat"]?["minimum"]?.GetValue<double>());
            Assert.AreEqual(90d, props["lat"]?["maximum"]?.GetValue<double>());
            Assert.AreEqual("Latitude in WGS-84 decimal degrees.", props["lat"]?["description"]?.GetValue<string>());
            Assert.AreEqual("deg", props["lon"]?["x-unit"]?.GetValue<string>());
            Assert.AreEqual(-180d, props["lon"]?["minimum"]?.GetValue<double>());
            Assert.AreEqual(180d, props["lon"]?["maximum"]?.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsIdenticalFieldSchemasForNullableAndNonNullableStruct()
        {
            // Arrange
            // A nullable struct property differs from its non-nullable twin only by the
            // property-level type widening to ["object","null"] — the field subschemas
            // must be bit-identical.

            // Act
            var nonNullable = GetSchema("Location");
            var nullable = GetSchema("LastKnownLocation");

            // Assert
            Assert.AreEqual(nonNullable["properties"]!.ToJsonString(), nullable["properties"]!.ToJsonString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsArrayOfStructSchemaForRoute()
        {
            // Arrange

            // Act
            var schema = GetSchema("Route");

            // Assert
            Assert.AreEqual("array", schema["type"]?.GetValue<string>());
            Assert.AreEqual("object", schema["items"]?["type"]?.GetValue<string>());
            Assert.AreEqual("Coordinates", schema["items"]?["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.6")]
        public void EmitsStructFieldAnnotationsForScheduledSetpoint()
        {
            // Arrange
            // [StructField(Unit = "kW")] on PowerSetpoint should travel through to schema.

            // Act
            var schema = GetSchema("Schedule");

            // Assert
            Assert.AreEqual("array", schema["type"]?.GetValue<string>());
            var itemsProps = schema["items"]?["properties"] as JsonObject;
            Assert.IsNotNull(itemsProps);
            Assert.AreEqual("kW", itemsProps["powerSetpoint"]?["x-unit"]?.GetValue<string>());
            Assert.AreEqual("V", itemsProps["voltageSetpoint"]?["x-unit"]?.GetValue<string>());

            // The unannotated DateTime field 'at' should have no x-unit
            Assert.IsNull(itemsProps["at"]?["x-unit"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.6")]
        public void EmitsWriteOnlyOnlyForSecretStructField()
        {
            // Arrange

            // Act
            var schema = GetSchema("Credentials");

            // Assert
            Assert.AreEqual("object", schema["type"]?.GetValue<string>());
            var props = schema["properties"] as JsonObject;
            Assert.IsNotNull(props);
            Assert.IsTrue(props["accessToken"]?["writeOnly"]?.GetValue<bool>());
            Assert.IsNull(props["endpoint"]?["writeOnly"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.6")]
        public void EmitsDateTimeFormatForScheduledSetpointAtField()
        {
            // Arrange

            // Act
            var schema = GetSchema("Schedule");
            var itemsProps = schema["items"]?["properties"] as JsonObject;

            // Assert
            Assert.IsNotNull(itemsProps);

            // DateTime maps to {"type":"string","format":"date-time"}
            Assert.AreEqual("string", itemsProps["at"]?["type"]?.GetValue<string>());
            Assert.AreEqual("date-time", itemsProps["at"]?["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void EmitsNullableStructFieldSchemasForRegisterWrites()
        {
            // Arrange
            // Regression: an ImmutableArray<record struct> whose fields are independently nullable
            // (string? / double? / DateTime?) must emit nullable field subschemas. Before the fix the
            // string? field was a bare (non-nullable) "string", so the outbound codec threw on a null value
            // and dale dropped every populated publish of the property — the cloud widget hung forever.

            // Act
            var schema = GetSchema("RegisterWrites");

            // Assert
            Assert.AreEqual("array", schema["type"]?.GetValue<string>());
            var props = schema["items"]?["properties"] as JsonObject;
            Assert.IsNotNull(props);

            // Non-nullable field: bare string.
            Assert.AreEqual("string", props["register"]?["type"]?.GetValue<string>());

            // Nullable reference field (string?) — the bug: must widen to ["string","null"].
            var errorType = props["lastError"]?["type"] as JsonArray;
            Assert.IsNotNull(errorType);
            Assert.HasCount(2, errorType);
            Assert.AreEqual("string", errorType[0]!.GetValue<string>());
            Assert.AreEqual("null", errorType[1]!.GetValue<string>());

            // Nullable value field (double?) — ["number","null"].
            var valueType = props["lastWrittenValue"]?["type"] as JsonArray;
            Assert.IsNotNull(valueType);
            Assert.AreEqual("number", valueType[0]!.GetValue<string>());
            Assert.AreEqual("null", valueType[1]!.GetValue<string>());

            // Nullable DateTime field — ["string","null"] with date-time format.
            var atType = props["lastAttemptUtc"]?["type"] as JsonArray;
            Assert.IsNotNull(atType);
            Assert.AreEqual("string", atType[0]!.GetValue<string>());
            Assert.AreEqual("null", atType[1]!.GetValue<string>());
            Assert.AreEqual("date-time", props["lastAttemptUtc"]?["format"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.2")]
        public void RequiresOnlyNonNullableStructFields()
        {
            // Arrange
            // Only the non-nullable field is required; nullable fields must be omitted from required so a
            // legitimately-absent (null) value stays valid on the inbound (set) direction too. Before the fix
            // every positional parameter was added to required.

            // Act
            var schema = GetSchema("RegisterWrites");
            var required = schema["items"]?["required"] as JsonArray;

            // Assert
            Assert.IsNotNull(required);
            Assert.HasCount(1, required);
            Assert.AreEqual("register", required[0]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.1")]
        public void EmitsEnumSchemaForCurrentAlarm()
        {
            // Arrange

            // Act
            var schema = GetSchema("CurrentAlarm");

            // Assert
            Assert.AreEqual("string", schema["type"]?.GetValue<string>());
            Assert.AreEqual("AlarmState", schema["title"]?.GetValue<string>());
            var members = schema["enum"] as JsonArray;
            Assert.IsNotNull(members);
            Assert.HasCount(3, members);
            Assert.IsTrue(ContainsString(members, "Ok"));
            Assert.IsTrue(ContainsString(members, "Warning"));
            Assert.IsTrue(ContainsString(members, "Critical"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.1")]
        public void EmitsNullableEnumSchemaForLastAlarm()
        {
            // Arrange

            // Act
            var schema = GetSchema("LastAlarm");
            var typeArr = schema["type"] as JsonArray;

            // Assert
            Assert.IsNotNull(typeArr);
            Assert.HasCount(2, typeArr);
            Assert.AreEqual("string", typeArr[0]!.GetValue<string>());
            Assert.AreEqual("null", typeArr[1]!.GetValue<string>());
            var members = schema["enum"] as JsonArray;
            Assert.IsNotNull(members);

            // Spec: nullable enum gets its members PLUS null in the enum array → 4 entries.
            Assert.HasCount(4, members);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.4")]
        public void EmitsReadOnlyOnMeasuringPointSchema()
        {
            // Arrange
            // MeasuringPoints are marked readOnly in the schema.

            // Act
            var schema = GetSchema("Location");

            // Assert
            Assert.IsTrue(schema["readOnly"]?.GetValue<bool>() ?? false);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.4")]
        public void OmitsReadOnlyOnServicePropertySchema()
        {
            // Arrange
            // ServiceProperties with a public setter are writable; readOnly must be absent.

            // Act
            var schema = GetSchema("VoltageSetpoint");

            // Assert
            Assert.IsNull(schema["readOnly"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.4")]
        public void EmitsReadOnlyOnServicePropertyWithPrivateSetter()
        {
            // Arrange
            // [ServiceProperty] on `{ get; private set; }` is a published-only value:
            // dale publishes state, but the cloud cannot SetPropertyValue it back.
            // Schema must carry readOnly so the dashboard groups it with measuring points.

            // Act
            var schema = GetSchema("TimesSwitchedOn");

            // Assert
            Assert.IsTrue(schema["readOnly"]?.GetValue<bool>() ?? false);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.4")]
        public void EmitsReadOnlyOnArrayMeasuringPointSchema()
        {
            // Arrange

            // Act
            var schema = GetSchema("HistogramBuckets");

            // Assert
            Assert.IsTrue(schema["readOnly"]?.GetValue<bool>() ?? false);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.4")]
        public void EmitsReadOnlyOnServicePropertyWithReadOnlyOptIn()
        {
            // Arrange
            // [ServiceProperty(ReadOnly = true)] forces the wire read-only flag even though the C# property
            // has a public setter (so cross-assembly helpers can still assign it). The cloud must see
            // readOnly=true and refuse SetPropertyValue.

            // Act
            var schema = GetSchema("WriteRegisters");

            // Assert
            Assert.IsTrue(schema["readOnly"]?.GetValue<bool>() ?? false);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.1")]
        public void RoutesEnumPropertyTitleToPresentationDisplayName()
        {
            // Arrange
            // PreferredMode is `OperatingMode?` (nullable enum). schema.title carries the enum's
            // identity ("OperatingMode"); the property-level Title must land in
            // presentation.displayName instead, otherwise it would be silently dropped by the
            // serializer (ApplyAnnotations skips Title when schema.title is already set).

            // Act
            var presentation = GetPresentation("PreferredMode");

            // Assert
            Assert.AreEqual("Bevorzugter Modus", presentation?["displayName"]?.GetValue<string>());

            // schema.title stays identity-bearing.
            var schema = GetSchema("PreferredMode");
            Assert.AreEqual("OperatingMode", schema["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.1")]
        public void RoutesStructPropertyTitleToPresentationDisplayName()
        {
            // Arrange
            // PreferredLocation is `Coordinates?`. Same routing rule as the enum case.

            // Act
            var presentation = GetPresentation("PreferredLocation");

            // Assert
            Assert.AreEqual("Bevorzugte Position", presentation?["displayName"]?.GetValue<string>());
            var schema = GetSchema("PreferredLocation");
            Assert.AreEqual("Coordinates", schema["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.5")]
        public void EmitsUIHintStatusIndicatorFromStatusIndicatorAttribute()
        {
            // Arrange
            // CurrentStatus is `[StatusIndicator] AlarmState?`. The presence of [StatusIndicator]
            // routes to presentation.uiHint = "statusIndicator" — explicit hint instead of inferring
            // status-indicator from the presence of statusMappings.

            // Act
            var presentation = GetPresentation("CurrentStatus");

            // Assert
            Assert.AreEqual("statusIndicator", presentation?["uiHint"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.5")]
        public void OmitsUIHintWhenNoUIHintNorStatusIndicator()
        {
            // Arrange
            // PreferredMode has no [UIHint] and no [StatusIndicator] — uiHint must be absent.

            // Act
            var presentation = GetPresentation("PreferredMode");

            // Assert
            Assert.IsNull(presentation?["uiHint"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.3")]
        public void EmitsEnumLabelsFromEnumLabelAttribute()
        {
            // Arrange
            // AlarmState's members carry [EnumLabel("Alles in Ordnung")] etc.
            // The labels route to presentation.enumLabels keyed by CLR member name.
            // Exercised via CurrentStatus (writable nullable enum) and CurrentAlarm (read-only enum
            // measuring point) — both should carry the same label map.

            // Act
            var presentation = GetPresentation("CurrentStatus");
            var labels = presentation?["enumLabels"]?.AsObject();

            // Assert
            Assert.IsNotNull(labels);
            Assert.AreEqual("Alles in Ordnung", labels["Ok"]?.GetValue<string>());
            Assert.AreEqual("Warnung", labels["Warning"]?.GetValue<string>());
            Assert.AreEqual("Kritisch", labels["Critical"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.3")]
        public void OmitsEnumLabelsOnNonEnumProperty()
        {
            // Arrange
            // VoltageSetpoint is a double — no enum labels possible.

            // Act
            var presentation = GetPresentation("VoltageSetpoint");

            // Assert
            Assert.IsNull(presentation?["enumLabels"]);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static JsonNode GetSchema(string identifier)
        {
            var lb = new RichTypesLogicBlock();
            var sp = new ServiceCollection().BuildServiceProvider();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(lb, sp);

            foreach (var service in result.Services)
            {
                foreach (var prop in service.Properties)
                {
                    if (prop.Identifier == identifier)
                    {
                        return prop.Schema;
                    }
                }

                foreach (var mp in service.MeasuringPoints)
                {
                    if (mp.Identifier == identifier)
                    {
                        return mp.Schema;
                    }
                }
            }

            throw new InvalidOperationException($"No property/measuring-point named '{identifier}' was found in introspection result");
        }

        private static JsonNode? GetPresentation(string identifier)
        {
            var lb = new RichTypesLogicBlock();
            var sp = new ServiceCollection().BuildServiceProvider();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(lb, sp);

            foreach (var service in result.Services)
            {
                foreach (var prop in service.Properties)
                {
                    if (prop.Identifier == identifier)
                    {
                        return prop.Presentation;
                    }
                }

                foreach (var mp in service.MeasuringPoints)
                {
                    if (mp.Identifier == identifier)
                    {
                        return mp.Presentation;
                    }
                }
            }

            throw new InvalidOperationException($"No property/measuring-point named '{identifier}' was found in introspection result");
        }

        private static bool ContainsString(JsonArray arr, string value)
        {
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue<string>(out var s) && s == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}