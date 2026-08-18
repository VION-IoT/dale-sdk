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

            public DiagnosticsPublishingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }
    }
}