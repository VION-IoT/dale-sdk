using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Introspection
{
    public readonly record struct Coordinates(
        [StructField(Unit = "deg", Minimum = -90, Maximum = 90, Description = "Latitude in WGS-84 decimal degrees.")]
        double Lat,
        [StructField(Unit = "deg", Minimum = -180, Maximum = 180, Description = "Longitude in WGS-84 decimal degrees.")]
        double Lon);

    public readonly record struct ScheduledSetpoint(DateTime At, [StructField(Unit = "kW")] double PowerSetpoint, [StructField(Unit = "V")] double VoltageSetpoint);

    // Flat record struct with a secret field — the per-member WriteOnly fixture. Only AccessToken carries
    // [StructField(WriteOnly = true)], so writeOnly must surface on that member's schema and nowhere else.
    // The token is string? so it can be cleared (null) — a non-nullable secret could never be cleared.
    public readonly record struct ConnectionCredentials(
        [StructField(Title = "Endpoint")] string Endpoint,
        [StructField(Title = "Access token", WriteOnly = true)]
        string? AccessToken);

    // A flat record struct with independently-nullable fields (string? / double? / DateTime?). The outbound
    // encode-regression fixture: a populated entry with null fields must emit JSON null per field — so the
    // schema must mark each nullable field as nullable and omit it from required — instead of the codec
    // throwing on the null and dale silently dropping the whole property publish.
    public readonly record struct RegisterWriteInfo(string Register, double? LastWrittenValue, string? LastError, DateTime? LastAttemptUtc);

    public enum AlarmState
    {
        [EnumLabel("Alles in Ordnung")]
        Ok,

        [EnumLabel("Warnung")]
        Warning,

        [EnumLabel("Kritisch")]
        Critical,
    }

    public class RichTypesLogicBlock : LogicBlockBase
    {
        // Primitives — including the new unsigned set
        [ServiceProperty]
        public bool BoolValue { get; set; }

        [ServiceProperty]
        public byte ByteValue { get; set; }

        [ServiceProperty]
        public ushort UShortValue { get; set; }

        [ServiceProperty]
        public uint UIntValue { get; set; }

        [ServiceProperty]
        public long LongValue { get; set; }

        [ServiceProperty(Unit = "V", Minimum = 0, Maximum = 400)]
        public double VoltageSetpoint { get; set; }

        // [ServiceProperty] with private setter — gateway publishes the value but cloud cannot write back.
        // Used to assert that readOnly is emitted even on a service property when the setter isn't public.
        [ServiceProperty(Title = "Anzahl Einschaltungen")]
        public int TimesSwitchedOn { get; private set; }

        // [ServiceProperty(ReadOnly = true)] with a PUBLIC setter — opt-out for properties that need a public
        // setter for cross-assembly helpers but must remain cloud-read-only. Symmetric to WriteOnly.
        [ServiceProperty(Title = "Letzte Register-Schreibvorgänge", ReadOnly = true)]
        public ImmutableArray<int> WriteRegisters { get; set; } = ImmutableArray<int>.Empty;

        // Nullable primitives
        [ServiceProperty]
        public double? Target { get; set; }

        [ServiceProperty]
        public int? OptionalCount { get; set; }

        // String — both annotated forms
        [ServiceProperty]
        public string NonNullName { get; set; } = string.Empty;

        [ServiceProperty]
        public string? OptionalErrorMessage { get; set; }

        // ImmutableArray<T> for various T
        [ServiceMeasuringPoint(Unit = "A")]
        public ImmutableArray<double> HistogramBuckets { get; private set; } = ImmutableArray<double>.Empty;

        [ServiceMeasuringPoint]
        public ImmutableArray<int?> SamplesWithGaps { get; private set; } = ImmutableArray<int?>.Empty;

        // Struct — direct, nullable, array, array-of-nullable
        [ServiceMeasuringPoint]
        public Coordinates Location { get; private set; }

        [ServiceMeasuringPoint]
        public Coordinates? LastKnownLocation { get; private set; }

        [ServiceMeasuringPoint]
        public ImmutableArray<Coordinates> Route { get; private set; } = ImmutableArray<Coordinates>.Empty;

        [ServiceProperty]
        public ImmutableArray<ScheduledSetpoint> Schedule { get; set; } = ImmutableArray<ScheduledSetpoint>.Empty;

        // Array of a record struct whose fields are independently nullable — the outbound encode regression.
        [ServiceProperty]
        public ImmutableArray<RegisterWriteInfo> RegisterWrites { get; set; } = ImmutableArray<RegisterWriteInfo>.Empty;

        // Enum — direct + nullable
        [ServiceMeasuringPoint]
        public AlarmState CurrentAlarm { get; private set; }

        [ServiceMeasuringPoint]
        public AlarmState? LastAlarm { get; private set; }

        // Nullable enum — writable service property (needed for ServiceBinder round-trip tests).
        // The Title here is the property's display label. schema.title is identity-bearing (the
        // enum CLR name), so the Title routes to presentation.displayName instead.
        [ServiceProperty(Title = "Bevorzugter Modus")]
        public OperatingMode? PreferredMode { get; set; }

        // Nullable struct — writable service property (needed for ServiceBinder round-trip tests).
        // Same identity-title routing as PreferredMode above.
        [ServiceProperty(Title = "Bevorzugte Position")]
        public Coordinates? PreferredLocation { get; set; }

        // [Presentation(StatusIndicator = true)] on a writable nullable enum — presentation.uiHint should be
        // "statusIndicator", letting dashboards detect status-indicator properties by an
        // explicit hint rather than inferring from StatusMappings presence.
        [ServiceProperty(Title = "Aktueller Status")]
        [Presentation(StatusIndicator = true)]
        public AlarmState? CurrentStatus { get; set; }

        // ImmutableArray — writable service property (needed for ServiceBinder round-trip tests)
        [ServiceProperty]
        public ImmutableArray<double> Setpoints { get; set; } = ImmutableArray<double>.Empty;

        // Writable struct property carrying a per-member secret — the [StructField(WriteOnly)] fixture.
        [ServiceProperty(Title = "Verbindungsdaten")]
        public ConnectionCredentials Credentials { get; set; }

        public RichTypesLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }
}