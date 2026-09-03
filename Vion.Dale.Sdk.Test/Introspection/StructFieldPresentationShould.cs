using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Test.Introspection
{
    /// <summary>
    ///     Status enum declared for use inside a struct. Members are deliberately NOT in alphabetical
    ///     order, so declaration order and sorted order are genuinely different documents (the ordering
    ///     test would prove nothing otherwise). <c>DeviceError</c> is multi-word: its label differs from
    ///     its member name, which is what makes the label assertion discriminating.
    /// </summary>
    public enum LinkOutcome
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("Online")]
        Online,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Device error")]
        DeviceError,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Zeitüberschreitung")]
        Timeout,

        [Severity(StatusSeverity.Neutral)]
        [EnumLabel("Abgebrochen")]
        Aborted,
    }

    /// <summary>
    ///     The VION-105 fixture. Three field shapes, one per rule:
    ///     <list type="bullet">
    ///         <item>
    ///             <c>State</c> — an enum field whose <c>schema.title</c> is identity-bearing, so its authored Title has
    ///             nowhere inline to go.
    ///         </item>
    ///         <item>
    ///             <c>LastFailureOutcome</c> — a <c>Nullable&lt;TEnum&gt;</c> field carrying no <c>[StructField]</c> at all:
    ///             labels and severities belong to the enum type, not the annotation.
    ///         </item>
    ///         <item>
    ///             <c>LastContactAt</c> — a scalar whose Title lands inline in <c>schema.title</c> and must therefore NOT be
    ///             duplicated into the presentation sibling.
    ///         </item>
    ///     </list>
    /// </summary>
    public readonly record struct LinkSummary(
        [StructField(Title = "Link state", Description = "Verdict of the last transaction.")]
        LinkOutcome State,
        LinkOutcome? LastFailureOutcome,
        [StructField(Title = "Last contact")] DateTime LastContactAt);

    [ServiceInterface]
    public interface ILinkDiagnostics
    {
        [ServiceProperty(Title = "Verbindung")]
        LinkSummary Link { get; }

        [ServiceMeasuringPoint]
        LinkSummary LinkTrend { get; }
    }

    /// <summary>
    ///     Exposes the same struct through all four emission paths, because they are four separate
    ///     <c>ExtractSiblings</c> call sites in <see cref="LogicBlockIntrospection" /> and a fix applied
    ///     to one of them leaves the other three broken.
    /// </summary>
    public class LinkSummaryLogicBlock : LogicBlockBase, ILinkDiagnostics
    {
        // Plain ("extra") service property — ProcessExtraPropertyBinding.
        [ServiceProperty(Title = "Gewünschte Verbindung")]
        public LinkSummary DesiredLink { get; set; }

        // Plain measuring point — ProcessExtraMeasuringPointBinding.
        [ServiceMeasuringPoint]
        public LinkSummary ObservedLink { get; private set; }

        public LinkSummaryLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        // Interface-bound service property — ProcessInterfacePropertyBinding.
        public LinkSummary Link { get; private set; }

        // Interface-bound measuring point — ProcessInterfaceMeasuringPointBinding.
        public LinkSummary LinkTrend { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    [TestClass]
    public class StructFieldPresentationShould
    {
        private readonly LogicBlockIntrospectionResult _result;

        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        public StructFieldPresentationShould()
        {
            _result = LogicBlockIntrospection.IntrospectLogicBlock(new LinkSummaryLogicBlock(), _serviceProvider);
        }

        [TestMethod]
        [DataRow("DesiredLink")]
        [DataRow("ObservedLink")]
        [DataRow("Link")]
        [DataRow("LinkTrend")]
        [TestProperty("spec", "AC-INTRO-011.1")]
        public void CarryEnumFieldAuthoredTitleOnEveryEmissionPath(string identifier)
        {
            // Arrange
            // The one thing [StructField(Title)] on an enum field cannot do is ride schema.title —
            // the type identity holds that slot. presentation.fields is the second slot.

            // Act
            var state = FieldEntry(identifier, "state");

            // Assert
            Assert.AreEqual("Link state", state["displayName"]?.GetValue<string>());
        }

        [TestMethod]
        [DataRow("DesiredLink")]
        [DataRow("ObservedLink")]
        [DataRow("Link")]
        [DataRow("LinkTrend")]
        [TestProperty("spec", "AC-INTRO-011.2")]
        public void CarryEnumLabelsAndSeveritiesOnEveryEmissionPath(string identifier)
        {
            // Arrange

            // Act
            var state = FieldEntry(identifier, "state");

            // "Device error" ≠ "DeviceError" — the multi-word member is the discriminating one. An
            // assertion on Online would pass without the fix, since that label equals its member name.

            // Assert
            Assert.AreEqual("Device error", (state["enumLabels"] as JsonObject)?["DeviceError"]?.GetValue<string>());

            // D3: severities are emitted unconditionally. Nothing here carries
            // [Presentation(StatusIndicator = true)] — that gate is a property-level concept only.
            Assert.AreEqual("error", (state["statusMappings"] as JsonObject)?["DeviceError"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-011.2")]
        public void LabelFieldCarryingNoStructFieldAttribute()
        {
            // Arrange
            // [StructField] is optional; labels and severities belong to the field's enum type. The field
            // is also a Nullable<TEnum>, so it exercises the peel.

            // Act
            var outcome = FieldEntry("DesiredLink", "lastFailureOutcome");

            // Assert
            Assert.AreEqual("Device error", (outcome["enumLabels"] as JsonObject)?["DeviceError"]?.GetValue<string>());
            Assert.AreEqual("error", (outcome["statusMappings"] as JsonObject)?["DeviceError"]?.GetValue<string>());

            // No [StructField], so no authored title to carry.
            Assert.IsNull(outcome["displayName"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-011.1")]
        public void LeaveScalarFieldAuthoredTitleInlineRatherThanDuplicatingIt()
        {
            // Arrange
            // "Last contact" lands in schema.title, which is free for a scalar. Emitting it in both
            // places would leave two sources with no rule about which wins.

            // Act
            var fields = Fields("DesiredLink");

            // Assert
            Assert.IsNull(fields["lastContactAt"], "A scalar field must not get a presentation entry.");
            Assert.AreEqual("Last contact", SchemaFields("DesiredLink")["lastContactAt"]?["title"]?.GetValue<string>());
        }

        [TestMethod]
        [DataRow("DesiredLink")]
        [DataRow("ObservedLink")]
        [DataRow("Link")]
        [DataRow("LinkTrend")]
        [TestProperty("spec", "AC-INTRO-011.1")]
        public void LeaveEnumFieldSchemaTitleCarryingItsTypeIdentity(string identifier)
        {
            // Arrange

            // Act
            var fields = SchemaFields(identifier);

            // Assert
            // schema.title is the type's identity, and the cloud keys that type's labels by it. The second
            // slot is an addition, not a replacement.
            Assert.AreEqual(nameof(LinkOutcome), fields["state"]?["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-003.2")]
        public void SerializeStructFieldPresentationInSortedKeyOrder()
        {
            // Arrange
            // VION-77 again, one level down: the nested maps are built as immutable dictionaries and .NET
            // randomizes string hashing per process, so without canonicalization the same assembly writes a
            // different `dale dev --export-config` on every run. As in the property-level test, the assertion
            // is sorted order rather than "two serializations agree" — two serializations inside one test
            // process share a hash seed and agree either way.

            // Act
            var fields = Fields("DesiredLink");

            // Constructor order is state, lastFailureOutcome — genuinely not the sorted order.

            // Assert
            CollectionAssert.AreEqual(new[] { "lastFailureOutcome", "state" }, fields.Select(entry => entry.Key).ToList());

            var state = FieldEntry("DesiredLink", "state");
            var expected = new[] { "Aborted", "DeviceError", "Online", "Timeout" };
            CollectionAssert.AreEqual(expected, (state["enumLabels"] as JsonObject)!.Select(entry => entry.Key).ToList());
            CollectionAssert.AreEqual(expected, (state["statusMappings"] as JsonObject)!.Select(entry => entry.Key).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-011.3")]
        public void LeaveStructWithNothingToCarryWithoutFieldsNode()
        {
            // Arrange
            // Coordinates' fields are scalars with no Title — every annotation lands inline, so an
            // otherwise-empty presentation must still serialize to null rather than to `{"fields":{}}`.

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new RichTypesLogicBlock(), _serviceProvider);
            var location = ServiceOf(result).MeasuringPoints.Single(m => m.Identifier == "Location");

            // Assert
            Assert.IsNull(location.Presentation);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-011.4")]
        public void KeepPropertyLevelPresentationItRidesAlongside()
        {
            // Arrange
            // The fields node is injected into the serialized presentation doc; the keys already there
            // must survive it. DesiredLink's own Title routes to displayName (its schema.title is the
            // struct's identity), which is exactly the key a naive overwrite would drop.

            // Act
            var presentation = PresentationOf("DesiredLink");

            // Assert
            Assert.AreEqual("Gewünschte Verbindung", presentation["displayName"]?.GetValue<string>());
            Assert.IsNotNull(presentation["fields"]);
        }

        private JsonObject FieldEntry(string identifier, string fieldName)
        {
            var entry = Fields(identifier)[fieldName] as JsonObject;
            Assert.IsNotNull(entry, $"{identifier}.presentation.fields.{fieldName} is missing.");
            return entry;
        }

        private JsonObject Fields(string identifier)
        {
            var fields = PresentationOf(identifier)["fields"] as JsonObject;
            Assert.IsNotNull(fields, $"{identifier} carries no presentation.fields.");
            return fields;
        }

        private JsonObject PresentationOf(string identifier)
        {
            var service = ServiceOf(_result);
            var presentation = service.Properties.FirstOrDefault(p => p.Identifier == identifier)?.Presentation ??
                               service.MeasuringPoints.FirstOrDefault(m => m.Identifier == identifier)?.Presentation;

            Assert.IsNotNull(presentation, $"{identifier} was not emitted, or carries no presentation.");
            return (JsonObject)presentation;
        }

        private JsonObject SchemaFields(string identifier)
        {
            var service = ServiceOf(_result);
            var schema = service.Properties.FirstOrDefault(p => p.Identifier == identifier)?.Schema ??
                         service.MeasuringPoints.FirstOrDefault(m => m.Identifier == identifier)?.Schema;

            Assert.IsNotNull(schema, $"{identifier} was not emitted.");
            return (JsonObject)schema["properties"]!;
        }

        /// <summary>
        ///     The block's root service, selected by identifier rather than by position: these fixtures
        ///     deliberately mix interface-bound and extra members, and a positional pick would fail as a
        ///     confusing missing-member assert if service ordering ever changed.
        /// </summary>
        private static LogicBlockIntrospectionResult.ServiceInfo ServiceOf(LogicBlockIntrospectionResult result)
        {
            return result.Services.Single(s => s.Identifier == nameof(LinkSummaryLogicBlock) || s.Identifier == nameof(RichTypesLogicBlock));
        }
    }
}