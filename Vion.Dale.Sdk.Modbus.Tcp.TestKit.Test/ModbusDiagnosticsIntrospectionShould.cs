using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     Pins that both summaries are publishable as ordinary service properties. Their whole point is that a block
    ///     author can surface link health on the dashboard without writing a projection, and that only holds if the
    ///     introspection the cloud reads accepts them — which it does only while every field stays a supported type and
    ///     the types stay flat readonly record structs, across the assembly boundary.
    /// </summary>
    [TestClass]
    public class ModbusDiagnosticsIntrospectionShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        public void EmitTheLinkSummaryAsAStructWithEveryFieldOfTheSummary()
        {
            // Arrange / Act
            var schema = SchemaOf(nameof(DiagnosticsPublishingBlock.Link));

            // Assert
            Assert.AreEqual("object", schema["type"]!.GetValue<string>());

            // Struct fields ride the wire camel-cased.
            var fields = schema["properties"]!.AsObject().Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("state", fields);
            Assert.Contains("lastContactAt", fields);
            Assert.Contains("lastFailureOutcome", fields);
            Assert.Contains("successCount", fields);
            Assert.Contains("expiredCount", fields);
            Assert.Contains("droppedCount", fields);
            Assert.Contains("lastRoundTrip", fields);
            Assert.Contains("maxQueuedWait", fields);
            Assert.Contains("queueDepth", fields);
            Assert.HasCount(18, fields, "Every field of the summary must reach the wire; a dropped one is a silent gap on the dashboard.");
        }

        [TestMethod]
        public void EmitTheConnectionSummaryAsAStruct()
        {
            // Arrange / Act
            var schema = SchemaOf(nameof(DiagnosticsPublishingBlock.Connection));

            // Assert
            Assert.AreEqual("object", schema["type"]!.GetValue<string>());
            var fields = schema["properties"]!.AsObject().Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("state", fields);
            Assert.Contains("consecutiveConnectFailures", fields);
            Assert.Contains("lastConnectDuration", fields);
            Assert.Contains("nextAttemptAt", fields);
            Assert.HasCount(9, fields);
        }

        [TestMethod]
        public void EmitTheLinkStateAsItsMemberNames()
        {
            // Arrange / Act
            var schema = SchemaOf(nameof(DiagnosticsPublishingBlock.Link));

            // Assert — the enum member names are translation keys in the cloud, so they are pinned here.
            var stateValues = schema["properties"]!["state"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            CollectionAssert.AreEqual(new[] { nameof(ModbusLinkState.Unknown), nameof(ModbusLinkState.Online), nameof(ModbusLinkState.Faulted) }, stateValues);
        }

        [TestMethod]
        public void LabelEveryLinkSummaryFieldSoItReadsWithoutAProjection()
        {
            // Arrange / Act
            var fields = SchemaOf(nameof(DiagnosticsPublishingBlock.Link))["properties"]!.AsObject();

            // Assert — the titles and descriptions are the cloud's translation source strings; every field carries
            // both, because a field that only shows its camelCase wire name is what this annotation pass removed.
            foreach (var (name, fieldSchema) in fields)
            {
                Assert.IsNotNull(fieldSchema!["title"], $"{name} has no title.");
                Assert.IsNotNull(fieldSchema["description"], $"{name} has no [StructField] Description.");
            }

            Assert.AreEqual("Last contact", fields["lastContactAt"]!["title"]!.GetValue<string>());
            Assert.AreEqual("Round trip (last)", fields["lastRoundTrip"]!["title"]!.GetValue<string>());
            Assert.AreEqual("Queue depth", fields["queueDepth"]!["title"]!.GetValue<string>());
        }

        [TestMethod]
        public void LabelEveryConnectionSummaryField()
        {
            // Arrange / Act
            var fields = SchemaOf(nameof(DiagnosticsPublishingBlock.Connection))["properties"]!.AsObject();

            // Assert
            foreach (var (name, fieldSchema) in fields)
            {
                Assert.IsNotNull(fieldSchema!["title"], $"{name} has no title.");
                Assert.IsNotNull(fieldSchema["description"], $"{name} has no [StructField] Description.");
            }

            Assert.AreEqual("Consecutive failed connects", fields["consecutiveConnectFailures"]!["title"]!.GetValue<string>());
            Assert.AreEqual("Last handshake", fields["lastConnectDuration"]!["title"]!.GetValue<string>());
        }

        [TestMethod]
        public void LeaveAnEnumFieldsTitleCarryingItsTypeIdentityRatherThanTheAuthoredLabel()
        {
            // Arrange / Act
            var link = SchemaOf(nameof(DiagnosticsPublishingBlock.Link))["properties"]!.AsObject();
            var connection = SchemaOf(nameof(DiagnosticsPublishingBlock.Connection))["properties"]!.AsObject();

            // Assert — the schema title of an enum-typed field is its CLR type name, not the field's
            // [StructField] Title: the type identity wins, the same way it does for an enum-typed property
            // (whose authored title is routed to presentation.displayName instead — a channel struct fields
            // do not have). The Description does land. A client must therefore fall back to the wire name
            // for a field whose title is identity-bearing; the DevHost does.
            Assert.AreEqual(nameof(ModbusLinkState), link["state"]!["title"]!.GetValue<string>());
            Assert.AreEqual(nameof(ModbusOutcome), link["lastFailureOutcome"]!["title"]!.GetValue<string>());
            Assert.AreEqual(nameof(ModbusTcpConnectionState), connection["state"]!["title"]!.GetValue<string>());
            Assert.StartsWith("Verdict of the last transaction", link["state"]!["description"]!.GetValue<string>());
        }

        [TestMethod]
        public void KeepTheTemporalFieldFormatsThatDriveDateAndDurationRendering()
        {
            // Arrange / Act
            var link = SchemaOf(nameof(DiagnosticsPublishingBlock.Link))["properties"]!.AsObject();

            // Assert — these come from the CLR type, not an attribute, and are what a client formats on.
            Assert.AreEqual("date-time", link["lastContactAt"]!["format"]!.GetValue<string>());
            Assert.AreEqual("duration", link["lastRoundTrip"]!["format"]!.GetValue<string>());
        }

        [TestMethod]
        public void MapTheLinkStateToSeveritiesAndLabelsWhenPublishedAsAStatusPill()
        {
            // Arrange / Act
            var presentation = PresentationOf(nameof(DiagnosticsPublishingBlock.LinkState));
            var severities = presentation["statusMappings"]!.AsObject();
            var labels = presentation["enumLabels"]!.AsObject();

            // Assert — a consumer publishing Link.State directly gets the pill colours from the SDK.
            Assert.AreEqual("neutral", severities[nameof(ModbusLinkState.Unknown)]!.GetValue<string>());
            Assert.AreEqual("success", severities[nameof(ModbusLinkState.Online)]!.GetValue<string>());
            Assert.AreEqual("error", severities[nameof(ModbusLinkState.Faulted)]!.GetValue<string>());
            Assert.AreEqual("Faulted", labels[nameof(ModbusLinkState.Faulted)]!.GetValue<string>());
        }

        [TestMethod]
        public void MapTheConnectionStateAndTheOutcomeToSeveritiesAndLabels()
        {
            // Arrange / Act
            var connection = PresentationOf(nameof(DiagnosticsPublishingBlock.ConnectionState));
            var outcome = PresentationOf(nameof(DiagnosticsPublishingBlock.LastFailureOutcome));

            // Assert
            Assert.AreEqual("warning", connection["statusMappings"]![nameof(ModbusTcpConnectionState.BackingOff)]!.GetValue<string>());
            Assert.AreEqual("Backing off", connection["enumLabels"]![nameof(ModbusTcpConnectionState.BackingOff)]!.GetValue<string>());

            var outcomeSeverities = outcome["statusMappings"]!.AsObject();
            Assert.AreEqual("warning", outcomeSeverities[nameof(ModbusOutcome.DeviceError)]!.GetValue<string>());
            Assert.AreEqual("error", outcomeSeverities[nameof(ModbusOutcome.TransportError)]!.GetValue<string>());
            Assert.AreEqual("neutral", outcomeSeverities[nameof(ModbusOutcome.Cancelled)]!.GetValue<string>());
            Assert.AreEqual("Transport error", outcome["enumLabels"]![nameof(ModbusOutcome.TransportError)]!.GetValue<string>());
        }

        private JsonObject PresentationOf(string propertyIdentifier)
        {
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new DiagnosticsPublishingBlock(), _serviceProvider);
            var service = result.Services.Single(s => s.Identifier == nameof(DiagnosticsPublishingBlock));

            return service.Properties.Single(p => p.Identifier == propertyIdentifier).Presentation!.AsObject();
        }

        private JsonObject SchemaOf(string propertyIdentifier)
        {
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new DiagnosticsPublishingBlock(), _serviceProvider);
            var service = result.Services.Single(s => s.Identifier == nameof(DiagnosticsPublishingBlock));

            return service.Properties.Single(p => p.Identifier == propertyIdentifier).Schema!.AsObject();
        }

        /// <summary>A block that does nothing but publish the two summaries — the shape a consumer surfaces them in.</summary>
        private sealed class DiagnosticsPublishingBlock : LogicBlockBase
        {
            [ServiceProperty]
            public ModbusLinkSummary Link { get; private set; }

            [ServiceProperty]
            public ModbusTcpConnectionSummary Connection { get; private set; }

            [ServiceProperty]
            [Presentation(StatusIndicator = true)]
            public ModbusLinkState LinkState
            {
                get => Link.State;
            }

            [ServiceProperty]
            [Presentation(StatusIndicator = true)]
            public ModbusTcpConnectionState ConnectionState
            {
                get => Connection.State;
            }

            [ServiceProperty]
            [Presentation(StatusIndicator = true)]
            public ModbusOutcome? LastFailureOutcome
            {
                get => Link.LastFailureOutcome;
            }

            public DiagnosticsPublishingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }
    }
}